// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Common;
using Microsoft.Internal.Common.Commands;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public enum CountersExportFormat { csv, json };

    internal static class Program
    {
        private static Command MonitorCommand()
        {
            Command monitorCommand = new(
                name: "monitor",
                description: "Start monitoring a .NET application")
            {
                CounterOption,
                ProcessIdOption,
                RefreshIntervalOption,
                NameOption,
                DiagnosticPortOption,
                ResumeRuntimeOption,
                MaxHistogramOption,
                MaxTimeSeriesOption,
                DurationOption,
                ShowDeltasOption
            };

            monitorCommand.TreatUnmatchedTokensAsErrors = false; // see the logic in Main

            monitorCommand.SetAction(static (parseResult, ct) => new CounterMonitor(parseResult.Configuration.Output, parseResult.Configuration.Error).Monitor(
                ct,
                counters: parseResult.GetValue(CounterOption),
                processId: parseResult.GetValue(ProcessIdOption),
                refreshInterval: parseResult.GetValue(RefreshIntervalOption),
                name: parseResult.GetValue(NameOption),
                diagnosticPort: parseResult.GetValue(DiagnosticPortOption) ?? string.Empty,
                resumeRuntime: parseResult.GetValue(ResumeRuntimeOption),
                maxHistograms: parseResult.GetValue(MaxHistogramOption),
                maxTimeSeries: parseResult.GetValue(MaxTimeSeriesOption),
                duration: parseResult.GetValue(DurationOption),
                showDeltas: parseResult.GetValue(ShowDeltasOption),
                dsrouter: string.Empty
            ));

            return monitorCommand;
        }

        private static Command CollectCommand()
        {
            Command collectCommand = new(
                name: "collect",
                description: "Monitor counters in a .NET application and export the result into a file")
            {
                CounterOption,
                ProcessIdOption,
                RefreshIntervalOption,
                ExportFormatOption,
                ExportFileNameOption,
                NameOption,
                DiagnosticPortOption,
                ResumeRuntimeOption,
                MaxHistogramOption,
                MaxTimeSeriesOption,
                DurationOption
            };

            collectCommand.TreatUnmatchedTokensAsErrors = false; // see the logic in Main

            collectCommand.SetAction((parseResult, ct) => new CounterMonitor(parseResult.Configuration.Output, parseResult.Configuration.Error).Collect(
                ct,
                counters: parseResult.GetValue(CounterOption),
                processId: parseResult.GetValue(ProcessIdOption),
                refreshInterval: parseResult.GetValue(RefreshIntervalOption),
                format: parseResult.GetValue(ExportFormatOption),
                output: parseResult.GetValue(ExportFileNameOption),
                name: parseResult.GetValue(NameOption),
                diagnosticPort: parseResult.GetValue(DiagnosticPortOption) ?? string.Empty,
                resumeRuntime: parseResult.GetValue(ResumeRuntimeOption),
                maxHistograms: parseResult.GetValue(MaxHistogramOption),
                maxTimeSeries: parseResult.GetValue(MaxTimeSeriesOption),
                duration: parseResult.GetValue(DurationOption),
                dsrouter: string.Empty));

            return collectCommand;
        }

        private static readonly Option<string> NameOption =
            new("--name", "-n")
            {
                Description = "The name of the process that will be monitored.",
            };

        private static Option<int> ProcessIdOption =
            new("--process-id", "-p")
            {
                Description = "The process id that will be monitored.",
            };

        private static Option<int> RefreshIntervalOption =
            new("--refresh-interval")
            {
                Description = "The number of seconds to delay between updating the displayed counters.",
                DefaultValueFactory = _ => 1
            };

        private static readonly Option<CountersExportFormat> ExportFormatOption =
            new("--format")
            {
                Description = "The format of exported counter data.",
                DefaultValueFactory = _ => CountersExportFormat.csv
            };

        private static readonly Option<string> ExportFileNameOption =
            new("--output", "-o")
            {
                Description = "The output file name.",
                DefaultValueFactory = _ => "counter"
            };

        private static readonly Option<string> CounterOption =
            new("--counters")
            {
                Description = "A comma-separated list of counter providers. Counter providers can be specified as <provider_name> or <provider_name>[comma_separated_counter_names]. If the provider_name" +
                " is used without qualifying counter_names then all counters will be shown. For example \"System.Runtime[dotnet.assembly.count,dotnet.gc.pause.time],Microsoft.AspNetCore.Hosting\"" +
                " includes the dotnet.assembly.count and dotnet.gc.pause.time counters from the System.Runtime provider and all the counters from the Microsoft.AspNetCore.Hosting provider. Provider" +
                " names can either refer to the name of a Meter for the System.Diagnostics.Metrics API or the name of an EventSource for the EventCounters API. If the monitored application has both" +
                " a Meter and an EventSource with the same name, the Meter is automatically preferred. Use the prefix \'EventCounters\\\' in front of a provider name to only show the EventCounters." +
                " To discover well-known provider and counter names, please visit https://learn.microsoft.com/dotnet/core/diagnostics/built-in-metrics."
            };

        private static Command ListCommand()
        {
            Command listCommand = new(
                name: "list",
                description: "Display a list of counter names and descriptions, grouped by provider.")
            {
                RuntimeVersionOption()
            };

            listCommand.SetAction((parseResult, ct) => Task.FromResult(List()));

            return listCommand;
        }

        private static Option<string> RuntimeVersionOption() =>
            new("--runtime-version", "-r")
            {
                Description = "Version of runtime. Supported runtime version: 3.0, 3.1, 5.0, 6.0, 7.0, 8.0",
                DefaultValueFactory = _ => "6.0"
            };

        private static readonly Option<string> DiagnosticPortOption =
            new("--diagnostic-port", "--dport")
            {
                Description = "The path to diagnostic port to be used.",
            };

        private static readonly Option<bool> ResumeRuntimeOption =
            new("--resume-runtime")
            {
                Description = "Resume runtime once session has been initialized, defaults to true. Disable resume of runtime using --resume-runtime:false",
                DefaultValueFactory = _ => true
            };

        private static readonly Option<int> MaxHistogramOption =
            new("--maxHistograms")
            {
                Description = "The maximum number of histograms that can be tracked. Each unique combination of provider name, histogram name, and dimension values" +
                " counts as one histogram. Tracking more histograms uses more memory in the target process so this bound guards against unintentional high memory use.",
                DefaultValueFactory = _ => 10
            };

        private static readonly Option<int> MaxTimeSeriesOption =
            new("--maxTimeSeries")
            {
                Description = "The maximum number of time series that can be tracked. Each unique combination of provider name, metric name, and dimension values" +
                " counts as one time series. Tracking more time series uses more memory in the target process so this bound guards against unintentional high memory use.",
                DefaultValueFactory = _ => 1000
            };

        private static readonly Option<TimeSpan> DurationOption =
            new("--duration")
            {
                Description = "When specified, will run for the given timespan and then automatically stop. Provided in the form of dd:hh:mm:ss."
            };

        private static readonly Option<bool> ShowDeltasOption =
            new("--showDeltas")
            {
                Description = @"Shows an extra column in the metrics table that displays the delta between the previous metric value and the most recent value." +
               " This is useful to monitor the rate of change for a metric."
            };

        public static int List()
        {
            Console.WriteLine("Counter information has been moved to the online .NET documentation.");
            Console.WriteLine("Please visit https://learn.microsoft.com/dotnet/core/diagnostics/built-in-metrics.");
            return 1;
        }

        private static Task<int> Main(string[] args)
        {
            RootCommand rootCommand = new()
            {
                MonitorCommand(),
                CollectCommand(),
                ListCommand(),
                ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that can be monitored.")
            };

            ParseResult parseResult = rootCommand.Parse(args);
            string parsedCommandName = parseResult.CommandResult.Command.Name;
            if (parsedCommandName is "monitor" or "collect")
            {
                IReadOnlyCollection<string> unparsedTokens = parseResult.UnmatchedTokens;
                // If we notice there are unparsed tokens, user might want to attach on startup.
                if (unparsedTokens.Count > 0)
                {
                    ProcessLauncher.Launcher.PrepareChildProcess(args);
                }
            }

            return parseResult.InvokeAsync();
        }
    }
}
