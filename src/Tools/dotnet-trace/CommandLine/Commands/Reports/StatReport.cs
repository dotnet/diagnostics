// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using System.IO;
using System.CommandLine.IO;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class StatReportHandler
    {
        private delegate Task<int> StatReportDelegate(CancellationToken ct, IConsole console, string traceFile, string filter, bool verbose);
        private static async Task<int> StatReport(CancellationToken ct, IConsole console, string traceFile, string filter, bool verbose) 
        {
            SimpleLogger.Log.MinimumLevel = verbose ? Microsoft.Extensions.Logging.LogLevel.Information : Microsoft.Extensions.Logging.LogLevel.Error;

            // Validate
            if (!File.Exists(traceFile))
            {
                console.Error.WriteLine($"The file '{traceFile}' doesn't exist.");
                return await Task.FromResult(-1);
            }

            // Parse the filter string
            Func<TraceEvent, bool> predicate = PredicateBuilder.ParseFilter(filter);

            using EventPipeEventSource source = new(traceFile);

            (Dictionary<string, int> stats, int total, string commandline, string osInformation, string archInformation) = CollectStats(source, predicate);

            PrintStats(console, source, stats, traceFile, total, commandline, osInformation, archInformation);
            return await Task.FromResult(0);
        }

        public static (Dictionary<string, int>, int, string, string, string) CollectStats(EventPipeEventSource source, Func<TraceEvent, bool> predicate)
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
Filter ::= 𝞮 | <NameFilter> | <NameFilter>:<Subfilter> | -<Filter> | <Filter>;<Filter>
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