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

#nullable enable

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class StatReportHandler
    {
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
                    eventNameInclude = $"{pattern}|{eventNameInclude}";
                }

                public void IncludeKeyword(long keyword)
                {
                    keywordInclude |= keyword;
                }

                public void IncludeEventId(int eventId)
                {
                    eventIdInclude.Add(eventId);
                }

                public Func<TraceEvent, bool> MakePredicate() => (TraceEvent data) =>
                {
                    bool ret = true;
                    if (data.ProviderName.Equals(ProviderName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(eventNameInclude))
                        {
                            ret &= Regex.IsMatch(data.EventName.ToLowerInvariant(), eventNameInclude.ToLowerInvariant());
                        }

                        if (keywordInclude != 0)
                        {
                            ret &= ((long)data.Keywords & keywordInclude) != 0;
                        }

                        if (eventIdInclude.Count != 0)
                        {
                            ret &= eventIdInclude.Contains((int)data.ID);
                        }
                    }

                    return ret;
                };
            }

            private readonly Dictionary<string, ProviderPredicate> providerPredicates = new();
            private string? providerNameIncludePattern;
            private string? providerNameExcludePattern;

            public PredicateBuilder() {}

            public PredicateBuilder AddProviderPattern(string pattern, bool exclude = false) => exclude switch
            {
                true => ExcludeProviderPattern(pattern),
                false => IncludeProviderPattern(pattern)
            };

            public PredicateBuilder ExcludeProviderPattern(string pattern)
            {
                providerNameExcludePattern = $"{pattern}|{providerNameExcludePattern}";
                return this;
            }

            public PredicateBuilder IncludeProviderPattern(string pattern)
            {
                providerNameIncludePattern = $"{pattern}|{providerNameIncludePattern}";
                return this;
            }

            public PredicateBuilder AddProviderFilter(string providerName, string eventNamePattern)
            {
                if (!providerPredicates.ContainsKey(providerName))
                    providerPredicates[providerName] = new ProviderPredicate(providerName);

                providerPredicates[providerName].IncludeEventName(eventNamePattern);
                return this;
            }

            public PredicateBuilder AddProviderFilter(string providerName, int eventId)
            {
                if (!providerPredicates.ContainsKey(providerName))
                    providerPredicates[providerName] = new ProviderPredicate(providerName);

                providerPredicates[providerName].IncludeEventId(eventId);
                return this;
            }

            public PredicateBuilder AddProviderFilter(string providerName, long keyword)
            {
                if (!providerPredicates.ContainsKey(providerName))
                    providerPredicates[providerName] = new ProviderPredicate(providerName);

                providerPredicates[providerName].IncludeKeyword(keyword);
                return this;
            }

            public Func<TraceEvent, bool> Build()
            {
                Func<TraceEvent, bool> predicate = (_) => true;

                if (string.IsNullOrEmpty(providerNameIncludePattern) && string.IsNullOrEmpty(providerNameExcludePattern) && providerPredicates.Count == 0)
                    return predicate;

                return predicate;
            }
        }

        private delegate Task<int> StatReportDelegate(CancellationToken ct, IConsole console, string traceFile, string filter, bool verbose);
        private static async Task<int> StatReport(CancellationToken ct, IConsole console, string traceFile, string filter, bool verbose) 
        {
            // Validate
            if (!File.Exists(traceFile))
            {
                console.Error.WriteLine($"The file '{traceFile}' doesn't exist.");
                return await Task.FromResult(-1);
            }

            // Parse the filter string
            Func<TraceEvent, bool> predicate = ParseFilter(filter);

            using EventPipeEventSource source = new(traceFile);

            Dictionary<string, int> stats = CollectStats(source, predicate);

            PrintStats(source, stats);
            return await Task.FromResult(0);
        }

        private static Dictionary<string, int> CollectStats(EventPipeEventSource source, Func<TraceEvent, bool> predicate)
        {
            Dictionary<string, int> stats = new();

            source.Dynamic.All += (TraceEvent data) =>
            {
                if (predicate(data))
                {
                    string key = $"{data.ProviderName}/{data.EventName}";
                    if (stats.ContainsKey(key))
                        stats[key]++;
                    else
                        stats[key] = 1;
                }
            };

            source.Process();

            return stats;
        }

        private static Func<TraceEvent, bool> ParseFilter(string filter)
        {
            // Filter ::= 𝞮 | <NameFilter> | <Name>:Subfilter | -Filter | Filter;Filter
            // Subfilter ::= id=<Number> | name=<NameFilter> | keyword=<Number>
            // NameFilter ::= [a-zA-Z0-9\*]+
            // Name ::= [a-zA-Z0-9]+
            // Number ::= [1-9]+[0-9]* | 0x[0-9a-fA-F]* | 0b[01]*

            Func<TraceEvent, bool> predicate = (_) => true;
            PredicateBuilder builder = new();

            string[] filters = filter.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (string f in filters)
            {
                bool exclude = false;
                if (f.StartsWith('-'))
                {
                    exclude = true;
                    f.TrimStart('-');
                }

                string[] filterParts = f.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (filterParts[0].Contains('*'))
                {
                    builder.AddProviderPattern(filterParts[0], exclude);
                }
                else
                {
                    // Regular name
                }


            }

            return builder.Build();
        }

        private static Func<TraceEvent, bool> MakeProviderNameWildcardPredicate(bool negate, string pattern, Func<TraceEvent, bool> previousPredicate) => negate switch
        {
            true => (TraceEvent data) => !Regex.IsMatch(data.ProviderName.ToLowerInvariant(), pattern.ToLowerInvariant()) && previousPredicate(data),
            false => (TraceEvent data) => !Regex.IsMatch(data.ProviderName.ToLowerInvariant(), pattern) && previousPredicate(data)
        };

        private static void PrintStats(EventPipeEventSource source, Dictionary<string, int> stats)
        {

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
* '-ProviderA:name=Jit*' - don't show Jit events from ProviderA
* '-Microsoft*' - don't show events from Microsoft* providers
";

        private static Option FilterOption()
        {
            return new Option(
                aliases: new[] {"--filter" },
                description: DescriptionString)
                {
                    Argument = new Argument<string>(name: "filter", getDefaultValue: () => "")
                };
        }         

        private static Option InclusiveOption() =>
            new Option(
                aliases: new[] { "--inclusive" },
                description: $"Output the top N methods based on inclusive time. If not specified, exclusive time is used by default.")
                {
                    Argument = new Argument<bool>(name: "inclusive", getDefaultValue: () => false)
                };

        public static Command StatCommand =>
            new Command(
                name: "topN",
                description: "Finds the top N methods that have been on the callstack the longest.")
                {
                    //Handler
                    HandlerDescriptor.FromDelegate((StatReportDelegate)StatReport).GetCommandHandler(),
                    FilterOption(),
                    InclusiveOption(),
                    ReportCommandHandler.VerboseOption(),
                };
    }
}