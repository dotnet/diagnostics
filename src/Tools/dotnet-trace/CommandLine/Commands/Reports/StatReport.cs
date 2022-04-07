using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tools.Trace.CommandLine;
using System.IO;
using System.CommandLine.IO;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class StatReportHandler
    {
        private class Logger
        {
            public static Logger Log = new();

            public bool Enabled { get; set; } = false;

            public void WriteLine(string message)
            {
                if (Enabled)
                    Console.WriteLine($"[{DateTime.Now:hh:mm:ss.fff}] {message}");
            }
        }
        private class PredicateBuilder
        {
            private class ProviderPredicate
            {
                public string ProviderName { get; private set; }
                private string eventNameInclude = "";
                private long keywordInclude = 0;
                private HashSet<int> eventIdInclude = new();

                public ProviderPredicate(string providerName)
                {
                    ProviderName = providerName;
                }

                public void IncludeEventName(string pattern)
                {
                    string sanitizedPattern = pattern.Replace("*", ".*");
                    eventNameInclude = $"{sanitizedPattern.ToLowerInvariant()}{(string.IsNullOrEmpty(eventNameInclude) ? "" : $"|{eventNameInclude}")}";
                }

                public void IncludeKeyword(long keyword)
                {
                    keywordInclude |= keyword;
                }

                public void IncludeEventId(int eventId)
                {
                    eventIdInclude.Add(eventId);
                }

                public bool CheckPredicate(TraceEvent data)
                {
                    bool ret = true;
                    if (!string.IsNullOrEmpty(eventNameInclude))
                    {
                        ret &= Regex.IsMatch(data.EventName.ToLowerInvariant(), eventNameInclude);
                    }

                    if (keywordInclude != 0)
                    {
                        ret &= ((long)data.Keywords & keywordInclude) != 0;
                    }

                    if (eventIdInclude.Count != 0)
                    {
                        ret &= eventIdInclude.Contains((int)data.ID);
                    }

                    return ret;
                }
            }

            private readonly Dictionary<string, ProviderPredicate> providerPredicates = new();
            private string providerNameIncludePattern;
            private string providerNameExcludePattern;

            public PredicateBuilder() {}

            public PredicateBuilder AddProviderPattern(string pattern, bool exclude = false) => exclude switch
            {
                true => ExcludeProviderPattern(pattern),
                false => IncludeProviderPattern(pattern)
            };

            public PredicateBuilder ExcludeProviderPattern(string pattern)
            {
                string sanitizedPattern = pattern.Replace("*", ".*");
                Logger.Log.WriteLine($"Adding exclude pattern: '{sanitizedPattern}'");
                providerNameExcludePattern = $"{sanitizedPattern.ToLowerInvariant()}{(string.IsNullOrEmpty(providerNameExcludePattern) ? "" : $"|{providerNameExcludePattern}")}";
                return this;
            }

            public PredicateBuilder IncludeProviderPattern(string pattern)
            {
                string sanitizedPattern = pattern.Replace("*", ".*");
                Logger.Log.WriteLine($"Adding include pattern: '{sanitizedPattern}'");
                providerNameIncludePattern = $"{sanitizedPattern.ToLowerInvariant()}{(string.IsNullOrEmpty(providerNameIncludePattern) ? "" : $"|{providerNameIncludePattern}")}";
                return this;
            }

            public PredicateBuilder AddProviderFilter(string providerName, string eventNamePattern)
            {
                string key = providerName.ToLowerInvariant();
                if (!providerPredicates.ContainsKey(key))
                    providerPredicates[key] = new ProviderPredicate(key);

                Logger.Log.WriteLine($"Adding event name filter: Provider={providerName}, pattern={eventNamePattern}");

                providerPredicates[key].IncludeEventName(eventNamePattern);
                return this;
            }

            public PredicateBuilder AddProviderFilter(string providerName, int eventId)
            {
                string key = providerName.ToLowerInvariant();
                if (!providerPredicates.ContainsKey(key))
                    providerPredicates[key] = new ProviderPredicate(key);

                Logger.Log.WriteLine($"Adding event id filter: Provider={providerName}, id={eventId}");

                providerPredicates[key].IncludeEventId(eventId);
                return this;
            }

            public PredicateBuilder AddProviderFilter(string providerName, long keyword)
            {
                string key = providerName.ToLowerInvariant();
                if (!providerPredicates.ContainsKey(key))
                    providerPredicates[key] = new ProviderPredicate(key);

                Logger.Log.WriteLine($"Adding event keyword filter: Provider={providerName}, keyword={keyword:X}");

                providerPredicates[key].IncludeKeyword(keyword);
                return this;
            }

            public Func<TraceEvent, bool> Build()
            {
                Func<TraceEvent, bool> predicate = (_) => true;

                if (string.IsNullOrEmpty(providerNameIncludePattern) && string.IsNullOrEmpty(providerNameExcludePattern) && providerPredicates.Count == 0)
                    return predicate;

                // order of checking:
                // 1. exclude regex
                // 2. include regex
                // 3. subfilters
                // effectively: exclude & include & (subfilters OR'd together)
                predicate = (TraceEvent data) =>
                {
                    bool ret = true;
                    if (!string.IsNullOrEmpty(providerNameExcludePattern))
                        ret &= !Regex.IsMatch(data.ProviderName.ToLowerInvariant(), providerNameExcludePattern);

                    if (!string.IsNullOrEmpty(providerNameIncludePattern))
                        ret &= Regex.IsMatch(data.ProviderName.ToLowerInvariant(), providerNameIncludePattern);

                    if (providerPredicates.Count != 0)
                    {
                        string key = data.ProviderName.ToLowerInvariant();
                        if (ret &= providerPredicates.TryGetValue(key, out ProviderPredicate providerPredicate))
                        {
                            ret &= providerPredicate.CheckPredicate(data);
                        }
                    }

                    return ret;
                };


                Logger.Log.WriteLine($"Include regex: {providerNameIncludePattern}");
                Logger.Log.WriteLine($"Exclude regex: {providerNameExcludePattern}");

                return predicate;
            }

            private Func<TraceEvent, bool> And(Func<TraceEvent, bool> p, Func<TraceEvent, bool> q) => (TraceEvent data) => p(data) && q(data);
        }

        private delegate Task<int> StatReportDelegate(CancellationToken ct, IConsole console, string traceFile, string filter, bool verbose);
        private static async Task<int> StatReport(CancellationToken ct, IConsole console, string traceFile, string filter, bool verbose) 
        {
            Logger.Log.Enabled = verbose;

            // Validate
            if (!File.Exists(traceFile))
            {
                console.Error.WriteLine($"The file '{traceFile}' doesn't exist.");
                return await Task.FromResult(-1);
            }

            // Parse the filter string
            Func<TraceEvent, bool> predicate = ParseFilter(filter);

            using EventPipeEventSource source = new(traceFile);

            (Dictionary<string, int> stats, int total, string commandline, string osInformation, string archInformation) = CollectStats(source, predicate);

            PrintStats(console, source, stats, traceFile, total, commandline, osInformation, archInformation);
            return await Task.FromResult(0);
        }

        private static (Dictionary<string, int>, int, string, string, string) CollectStats(EventPipeEventSource source, Func<TraceEvent, bool> predicate)
        {
            Dictionary<string, int> stats = new();
            int total = 0;
            string commandline = "";
            string osInformation = "";
            string archInformation = "";

            void HandleData(TraceEvent data)
            {
                total++;
                if (data.ProviderName.Equals("Microsoft-DotNETCore-EventPipe", StringComparison.InvariantCultureIgnoreCase) &&
                    data.EventName.Equals("ProcessInfo", StringComparison.InvariantCultureIgnoreCase))
                {
                    commandline = (string)data.PayloadByName("CommandLine");
                    osInformation = (string)data.PayloadByName("OSInformation");
                    archInformation = (string)data.PayloadByName("ArchInformation");
                }

                if (predicate(data))
                {
                    string key = $"{data.ProviderName}/{data.EventName}";
                    if (stats.ContainsKey(key))
                        stats[key]++;
                    else
                        stats[key] = 1;
                }
            }

            source.Dynamic.All += HandleData;

            source.Clr.All += HandleData;

            var parser = new Tracing.Parsers.ClrPrivateTraceEventParser(source);
            parser.All += HandleData;

            var rundownParser = new Tracing.Parsers.Clr.ClrRundownTraceEventParser(source);
            rundownParser.All += HandleData;

            source.Process();

            return (stats, total, commandline, osInformation, archInformation);
        }

        private static Func<TraceEvent, bool> ParseFilter(string filter)
        {
            // Filter ::= 𝞮 | <NameFilter> | <Name>:Subfilter | -Filter | Filter;Filter
            // Subfilter ::= id=<Number> | name=<NameFilter> | keyword=<Number>
            // NameFilter ::= [a-zA-Z0-9\*]+
            // Name ::= [a-zA-Z0-9]+
            // Number ::= [1-9]+[0-9]* | 0x[0-9a-fA-F]* | 0b[01]*

            PredicateBuilder builder = new();

            string[] filters = filter.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (string f in filters)
            {
                string filterSection = f;
                bool exclude = false;
                if (filterSection.StartsWith('-'))
                {
                    exclude = true;
                    filterSection = filterSection.TrimStart('-');
                }

                string[] filterParts = filterSection.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (filterParts[0].Contains('*') || filterParts.Length == 1)
                {
                    builder.AddProviderPattern(filterParts[0], exclude);
                }
                else if (filterParts.Length == 2)
                {
                    // Regular name
                    string[] subfilterParts = filterParts[1].Split('=', StringSplitOptions.RemoveEmptyEntries);
                    // TODO: DEBUG.ASSERT(length == 2)
                    if (subfilterParts.Length != 2)
                    {
                        // TODO error
                    }

                    switch (subfilterParts[0].ToLowerInvariant())
                    {
                        case "id":
                            if (int.TryParse(subfilterParts[1], out int id))
                                builder.AddProviderFilter(filterParts[0], id);
                            else
                                Logger.Log.WriteLine($"FILTER ERROR :: Failed to parse int from '{subfilterParts[1]}'");
                            break;
                        case "name":
                            builder.AddProviderFilter(filterParts[0], subfilterParts[1]);
                            break;
                        case "keyword":
                            if (TryParseLong(subfilterParts[1], out long keyword))
                                builder.AddProviderFilter(filterParts[0], keyword);
                            else
                                Logger.Log.WriteLine($"FILTER ERROR :: Failed to parse long from '{subfilterParts[1]}'");
                            break;
                        default:
                            Logger.Log.WriteLine($"FILTER ERROR :: Unknown subfilter '{subfilterParts[0]}'");
                            break;
                    }
                }
                else
                {
                    Logger.Log.WriteLine($"FILTER ERROR :: Invalid Filter '{f}");
                }
            }

            return builder.Build();
        }

        private static bool TryParseLong(string str, out long result)
        {
            bool ret = false;
            result = 0;
            if (str.StartsWith("0x") || str.StartsWith("0X"))
            {
                ret = long.TryParse(str, System.Globalization.NumberStyles.AllowHexSpecifier | System.Globalization.NumberStyles.HexNumber, null, out long val);
                result = val;
            }
            else if (str.StartsWith("0b") || str.StartsWith("0B"))
            {
                // Parse Binary
                int shift = 0;
                for (int i = str.Length - 1; i > 1; i--)
                {
                    if (str[i] == '1')
                        result |= 1L << shift;
                    else if (str[i] != '0')
                        return false;
                    shift++;
                }
                ret = true;
            }
            else
            {
                ret = long.TryParse(str, out long val);
                result = val;
            }
            return ret;
        }

        private static void PrintStats(IConsole console, EventPipeEventSource source, Dictionary<string, int> stats, string traceFile, int total, string commandline, string osInformation, string archInformation)
        {
            string divider = new('-', 120);
            // Print header info
            const int headerKeyAlignment = -30;
            const int headerValAlignment = 90;
            console.Out.WriteLine($"{"Trace name:",headerKeyAlignment}{traceFile,headerValAlignment}");
            console.Out.WriteLine($"{"Commandline:",headerKeyAlignment}{commandline,headerValAlignment}");
            console.Out.WriteLine($"{"OS:",headerKeyAlignment}{osInformation,headerValAlignment}");
            console.Out.WriteLine($"{"Architecture:",headerKeyAlignment}{archInformation,headerValAlignment}");
            console.Out.WriteLine($"{"Trace start time:",headerKeyAlignment}{source.SessionStartTime.ToLocalTime(),headerValAlignment}");
            console.Out.WriteLine($"{"Trace Duration:",headerKeyAlignment}{source.SessionDuration,headerValAlignment:c}");
            console.Out.WriteLine($"{"Number of processors:",headerKeyAlignment}{source.NumberOfProcessors,headerValAlignment}");
            console.Out.WriteLine($"{"Events Lost:",headerKeyAlignment}{source.EventsLost,headerValAlignment}");
            console.Out.WriteLine($"{"Total Events:",headerKeyAlignment}{total,headerValAlignment}");
            console.Out.WriteLine($"{"Filtered Events:",headerKeyAlignment}{stats.Values.Sum(),headerValAlignment}");
            console.Out.WriteLine(divider);
            console.Out.WriteLine();

            const int bodyKeyAlignment = -100;
            const int bodyValAlignment = 20;
            foreach ((string key, int val) in stats)
                console.Out.WriteLine($"{$"{key}:",bodyKeyAlignment}{val,bodyValAlignment}");
        }

        private const string DescriptionString = @$"Filter the report output. Syntax:
Filter ::= 𝞮 | <NameFilter> | <NameFilter>:Subfilter | -Filter | Filter;Filter
Subfilter ::= id=<Number> | name=<NameFilter> | keyword=<Number>
NameFilter ::= [a-zA-Z0-9\*]*
Number ::= [1-9]+[0-9]* | 0x[0-9a-fA-F]* | 0b[01]*

Examples:
* 'Microsoft-Windows-DotNETRuntime' - only show stats for this provider
* 'Microsoft-Windows-DotNETRuntime:name=Jit*' - only show stats for Jit events from this provider
* 'Microsoft-Windows-DotNETRuntime:name=Jit*;MyProvider:keyword=0xFFF' - only show stats for Jit events from this provider and events with keyword 0xFFF from the other
* '-ProviderA' - don't show events from ProviderA
* '-Microsoft*' - don't show events from Microsoft* providers
";

        private static Option FilterOption() =>
            new Option(
                aliases: new[] {"--filter" },
                description: DescriptionString)
                {
                    Argument = new Argument<string>(name: "filter", getDefaultValue: () => "")
                };

        private static Option VerboseOption() =>
            new Option(
                aliases: new[] {"-v", "--verbose"},
                description: $"Output additional information from filter parsing.")
                {
                    Argument = new Argument<bool>(name: "verbose", getDefaultValue: () => false)
                };

        public static Command StatCommand =>
            new Command(
                name: "stat",
                description: "Display information about number of events from each provider and metadata in the trace.")
                {
                    //Handler
                    HandlerDescriptor.FromDelegate((StatReportDelegate)StatReport).GetCommandHandler(),
                    FilterOption(),
                    VerboseOption(),
                    ReportCommandHandler.FileNameArgument()
                };
    }
}