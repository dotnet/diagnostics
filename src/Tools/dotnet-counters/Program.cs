// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Internal.Common.Commands;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;
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

namespace Microsoft.Diagnostics.Tools.Counters
{
    public enum CountersExportFormat { csv, json };

    internal class Program
    {
        delegate Task<int> CollectDelegate(CancellationToken ct, List<string> counter_list, string counters, IConsole console, int processId, int refreshInterval, CountersExportFormat format, string output, string processName, string port);
        delegate Task<int> MonitorDelegate(CancellationToken ct, List<string> counter_list, string counters, IConsole console, int processId, int refreshInterval, string processName, string port);

        private static Command MonitorCommand() =>
            new Command(
                name: "monitor",
                description: "Start monitoring a .NET application")
            {
                // Handler
                HandlerDescriptor.FromDelegate((MonitorDelegate)new CounterMonitor().Monitor).GetCommandHandler(),
                // Arguments and Options
                CounterList(), CounterOption(), ProcessIdOption(), RefreshIntervalOption(), NameOption(), DiagnosticPortOption(),
            };
        
        private static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Monitor counters in a .NET application and export the result into a file")
            {
                // Handler
                HandlerDescriptor.FromDelegate((CollectDelegate)new CounterMonitor().Collect).GetCommandHandler(),
                // Arguments and Options
                CounterList(), CounterOption(), ProcessIdOption(), RefreshIntervalOption(), ExportFormatOption(), ExportFileNameOption(), NameOption(), DiagnosticPortOption()
            };

        private static Option NameOption() =>
            new Option(
                aliases: new[] { "-n", "--name" },
                description: "The name of the process that will be monitored.")
            {
                Argument = new Argument<string>(name: "name")
            };

        private static Option ProcessIdOption() =>
            new Option(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id that will be monitored.")
            {
                Argument = new Argument<int>(name: "pid")
            };

        private static Option RefreshIntervalOption() =>
            new Option(
                alias: "--refresh-interval",
                description: "The number of seconds to delay between updating the displayed counters.")
            {
                Argument = new Argument<int>(name: "refresh-interval", getDefaultValue: () => 1)
            };

        private static Option ExportFormatOption() =>
            new Option(
                alias: "--format",
                description: "The format of exported counter data.")
            {
                Argument = new Argument<CountersExportFormat>(name: "format", getDefaultValue: () => CountersExportFormat.csv)
            };

        private static Option ExportFileNameOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: "The output file name.") 
            {
                Argument = new Argument<string>(name: "output", getDefaultValue: () => "counter")
            };

        private static Option CounterOption() =>
            new Option(
                alias: "--counters",
                description: "A comma-separated list of counter providers. Counter providers can be specified provider_name[:counter_name]. If the provider_name is used without a qualifying counter_name then all counters will be shown. To discover provider and counter names, use the list command.")
            {
                Argument = new Argument<string>(name: "counters", getDefaultValue: () => "System.Runtime")
            };

        private static Argument CounterList() =>
            new Argument<List<string>>(name: "counter_list", getDefaultValue: () => new List<string>())
            {
                Description = @"A space separated list of counters. Counters can be specified provider_name[:counter_name]. If the provider_name is used without a qualifying counter_name then all counters will be shown. To discover provider and counter names, use the list command.",
                IsHidden = true
            };

        private static Command ListCommand() =>
            new Command(
                name: "list",
                description: "Display a list of counter names and descriptions, grouped by provider.")
            {
                CommandHandler.Create<IConsole, string>(List),
                RuntimeVersionOption()
            };

        private static Option RuntimeVersionOption() =>
            new Option(
                aliases: new[] { "-r", "--runtime-version" },
                description: "Version of runtime. Supported runtime version: 3.0, 3.1, 5.0") 
            {
                Argument = new Argument<string>(name: "runtimeVersion", getDefaultValue: () => "3.1")
            };

        private static Option DiagnosticPortOption() =>
            new Option(
                alias: "--diagnostic-port",
                description: "The path to diagnostic port")
            {
                Argument = new Argument<string>(name: "diagnosticPort", getDefaultValue: () => "")
            };

        private static readonly string[] s_SupportedRuntimeVersions = new[] { "3.0", "3.1", "5.0" };

        public static int List(IConsole console, string runtimeVersion)
        {
            if (!s_SupportedRuntimeVersions.Contains(runtimeVersion))
            {
                Console.WriteLine($"{runtimeVersion} is not a supported version string or a supported runtime version.");
                Console.WriteLine("Supported version strings: 3.0, 3.1, 5.0");
                return 0;
            }
            var profiles = KnownData.GetAllProviders(runtimeVersion);
            var maxNameLength = profiles.Max(p => p.Name.Length);
            Console.WriteLine($"Showing well-known counters for .NET (Core) version {runtimeVersion} only. Specific processes may support additional counters.");
            foreach (var profile in profiles)
            {
                var counters = profile.GetAllCounters();
                var maxCounterNameLength = counters.Max(c => c.Name.Length);
                Console.WriteLine($"{profile.Name.PadRight(maxNameLength)}");
                foreach (var counter in profile.Counters.Values)
                {
                    Console.WriteLine($"    {counter.Name.PadRight(maxCounterNameLength)} \t\t {counter.Description}");
                }
                Console.WriteLine("");
            }
            return 1;
        }

        private static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .AddCommand(MonitorCommand())
                .AddCommand(CollectCommand())
                .AddCommand(ListCommand())
                .AddCommand(ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that can be monitored"))
                .UseDefaults()
                .Build();

            ParseResult parseResult = parser.Parse(args);
            string parsedCommandName = parseResult.CommandResult.Command.Name;
            if (parsedCommandName == "monitor" || parsedCommandName == "collect")
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
