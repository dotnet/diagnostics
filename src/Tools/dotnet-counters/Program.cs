// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Common.Commands;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public enum CountersExportFormat { csv, json };

    internal static class Program
    {
        private delegate Task<int> CollectDelegate(
            CancellationToken ct,
            List<string> counter_list,
            string counters,
            IConsole console,
            int processId,
            int refreshInterval,
            CountersExportFormat format,
            string output,
            string processName,
            string port,
            bool resumeRuntime,
            int maxHistograms,
            int maxTimeSeries,
            TimeSpan duration);

        private delegate Task<int> MonitorDelegate(
            CancellationToken ct,
            List<string> counter_list,
            string counters,
            IConsole console,
            int processId,
            int refreshInterval,
            string processName,
            string port,
            bool resumeRuntime,
            int maxHistograms,
            int maxTimeSeries,
            TimeSpan duration);

        private static Command MonitorCommand() =>
            new(
                name: "monitor",
                description: "Start monitoring a .NET application")
            {
                // Handler
                HandlerDescriptor.FromDelegate((MonitorDelegate)new CounterMonitor().Monitor).GetCommandHandler(),
                // Arguments and Options
                CounterList(),
                CounterOption(),
                ProcessIdOption(),
                RefreshIntervalOption(),
                NameOption(),
                DiagnosticPortOption(),
                ResumeRuntimeOption(),
                MaxHistogramOption(),
                MaxTimeSeriesOption(),
                DurationOption()
            };

        private static Command CollectCommand() =>
            new(
                name: "collect",
                description: "Monitor counters in a .NET application and export the result into a file")
            {
                // Handler
                HandlerDescriptor.FromDelegate((CollectDelegate)new CounterMonitor().Collect).GetCommandHandler(),
                // Arguments and Options
                CounterList(),
                CounterOption(),
                ProcessIdOption(),
                RefreshIntervalOption(),
                ExportFormatOption(),
                ExportFileNameOption(),
                NameOption(),
                DiagnosticPortOption(),
                ResumeRuntimeOption(),
                MaxHistogramOption(),
                MaxTimeSeriesOption(),
                DurationOption()
            };

        private static Option NameOption() =>
            new(
                aliases: new[] { "-n", "--name" },
                description: "The name of the process that will be monitored.")
            {
                Argument = new Argument<string>(name: "name")
            };

        private static Option ProcessIdOption() =>
            new(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id that will be monitored.")
            {
                Argument = new Argument<int>(name: "pid")
            };

        private static Option RefreshIntervalOption() =>
            new(
                alias: "--refresh-interval",
                description: "The number of seconds to delay between updating the displayed counters.")
            {
                Argument = new Argument<int>(name: "refresh-interval", getDefaultValue: () => 1)
            };

        private static Option ExportFormatOption() =>
            new(
                alias: "--format",
                description: "The format of exported counter data.")
            {
                Argument = new Argument<CountersExportFormat>(name: "format", getDefaultValue: () => CountersExportFormat.csv)
            };

        private static Option ExportFileNameOption() =>
            new(
                aliases: new[] { "-o", "--output" },
                description: "The output file name.")
            {
                Argument = new Argument<string>(name: "output", getDefaultValue: () => "counter")
            };

        private static Option CounterOption() =>
            new(
                alias: "--counters",
                description: "A comma-separated list of counter providers. Counter providers can be specified as <provider_name> or <provider_name>[comma_separated_counter_names]. If the provider_name is used without qualifying counter_names then all counters will be shown. For example \"System.Runtime[cpu-usage,working-set],Microsoft.AspNetCore.Hosting\" includes the cpu-usage and working-set counters from the System.Runtime provider and all the counters from the Microsoft.AspNetCore.Hosting provider. To discover provider and counter names, use the list command.")
            {
                Argument = new Argument<string>(name: "counters")
            };

        private static Argument CounterList() =>
            new Argument<List<string>>(name: "counter_list", getDefaultValue: () => new List<string>())
            {
                Description = @"A space separated list of counter providers. Counters can be specified <provider_name> or <provider_name>[comma_separated_counter_names]. If the provider_name is used without a qualifying counter_names then all counters will be shown. To discover provider and counter names, use the list command.",
                IsHidden = true
            };

        private static Command ListCommand() =>
            new(
                name: "list",
                description: "Display a list of counter names and descriptions, grouped by provider.")
            {
                CommandHandler.Create<IConsole, string>(List),
                RuntimeVersionOption()
            };

        private static Option RuntimeVersionOption() =>
            new(
                aliases: new[] { "-r", "--runtime-version" },
                description: "Version of runtime. Supported runtime version: 3.0, 3.1, 5.0, 6.0, 7.0, 8.0")
            {
                Argument = new Argument<string>(name: "runtimeVersion", getDefaultValue: () => "6.0")
            };

        private static Option DiagnosticPortOption() =>
            new(
                alias: "--diagnostic-port",
                description: "The path to diagnostic port to be used.")
            {
                Argument = new Argument<string>(name: "diagnosticPort", getDefaultValue: () => "")
            };

        private static Option ResumeRuntimeOption() =>
            new(
                alias: "--resume-runtime",
                description: @"Resume runtime once session has been initialized, defaults to true. Disable resume of runtime using --resume-runtime:false")
            {
                Argument = new Argument<bool>(name: "resumeRuntime", getDefaultValue: () => true)
            };

        private static Option MaxHistogramOption() =>
            new(
                alias: "--maxHistograms",
                description: "The maximum number of histograms that can be tracked. Each unique combination of provider name, histogram name, and dimension values" +
                " counts as one histogram. Tracking more histograms uses more memory in the target process so this bound guards against unintentional high memory use.")
            {
                Argument = new Argument<int>(name: "maxHistograms", getDefaultValue: () => 10)
            };

        private static Option MaxTimeSeriesOption() =>
            new(
                alias: "--maxTimeSeries",
                description: "The maximum number of time series that can be tracked. Each unique combination of provider name, metric name, and dimension values" +
                " counts as one time series. Tracking more time series uses more memory in the target process so this bound guards against unintentional high memory use.")
            {
                Argument = new Argument<int>(name: "maxTimeSeries", getDefaultValue: () => 1000)
            };

        private static Option DurationOption() =>
            new(
                alias: "--duration",
                description: @"When specified, will run for the given timespan and then automatically stop. Provided in the form of dd:hh:mm:ss.")
            {
                Argument = new Argument<TimeSpan>(name: "duration-timespan", getDefaultValue: () => default)
            };

        private static readonly string[] s_SupportedRuntimeVersions = KnownData.s_AllVersions;

        public static int List(IConsole console, string runtimeVersion)
        {
            if (!s_SupportedRuntimeVersions.Contains(runtimeVersion))
            {
                Console.WriteLine($"{runtimeVersion} is not a supported version string or a supported runtime version.");
                Console.WriteLine("Supported version strings: 3.0, 3.1, 5.0, 6.0, 7.0, 8.0");
                return 0;
            }
            IReadOnlyList<CounterProvider> profiles = KnownData.GetAllProviders(runtimeVersion);
            int maxNameLength = profiles.Max(p => p.Name.Length);
            Console.WriteLine($"Showing well-known counters for .NET (Core) version {runtimeVersion} only. Specific processes may support additional counters.");
            foreach (CounterProvider profile in profiles)
            {
                IReadOnlyList<CounterProfile> counters = profile.GetAllCounters();
                int maxCounterNameLength = counters.Max(c => c.Name.Length);
                Console.WriteLine($"{profile.Name.PadRight(maxNameLength)}");
                foreach (CounterProfile counter in profile.Counters.Values)
                {
                    Console.WriteLine($"    {counter.Name.PadRight(maxCounterNameLength)} \t\t {counter.Description}");
                }
                Console.WriteLine("");
            }
            return 1;
        }

        private static Task<int> Main(string[] args)
        {
            Parser parser = new CommandLineBuilder()
                .AddCommand(MonitorCommand())
                .AddCommand(CollectCommand())
                .AddCommand(ListCommand())
                .AddCommand(ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that can be monitored."))
                .UseDefaults()
                .Build();

            ParseResult parseResult = parser.Parse(args);
            string parsedCommandName = parseResult.CommandResult.Command.Name;
            if (parsedCommandName is "monitor" or "collect")
            {
                IReadOnlyCollection<string> unparsedTokens = parseResult.UnparsedTokens;
                // If we notice there are unparsed tokens, user might want to attach on startup.
                if (unparsedTokens.Count > 0)
                {
                    ProcessLauncher.Launcher.PrepareChildProcess(args);
                }
            }

            return parser.InvokeAsync(args);
        }
    }
}
