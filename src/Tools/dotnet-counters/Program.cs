// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Commands;
using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public enum CountersExportFormat { csv, json };

    internal class Program
    {
        delegate Task<int> ExportDelegate(CancellationToken ct, List<string> counter_list, IConsole console, int processId, int refreshInterval, CountersExportFormat format, string output, string processName);

        private static Command MonitorCommand() =>
            new Command(
                name: "monitor",
                description: "Start monitoring a .NET application")
            {
                // Handler
                CommandHandler.Create<CancellationToken, List<string>, IConsole, int, int, string>(new CounterMonitor().Monitor),
                // Arguments and Options
                CounterList(), ProcessIdOption(), RefreshIntervalOption(), NameOption()
            };

        private static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Monitor counters in a .NET application and export the result into a file")
            {
                // Handler
                HandlerDescriptor.FromDelegate((ExportDelegate)new CounterMonitor().Collect).GetCommandHandler(),
                // Arguments and Options
                CounterList(), ProcessIdOption(), RefreshIntervalOption(), ExportFormatOption(), ExportFileNameOption(), NameOption()
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
                Argument = new Argument<int>(name: "refresh-interval", defaultValue: 1)
            };

        private static Option ExportFormatOption() =>
            new Option(
                alias: "--format",
                description: "The format of exported counter data.")
            {
                Argument = new Argument<CountersExportFormat>(name: "format", defaultValue: CountersExportFormat.csv)
            };

        private static Option ExportFileNameOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: "The output file name.") 
            {
                Argument = new Argument<string>(name: "output", defaultValue: "counter")
            };

        private static Argument CounterList() =>
            new Argument<List<string>>(name: "counter_list", defaultValue: new List<string>()) 
            {
                Description = @"A space separated list of counters. Counters can be specified provider_name[:counter_name]. If the provider_name is used without a qualifying counter_name then all counters will be shown. To discover provider and counter names, use the list command.",
                Arity = ArgumentArity.ZeroOrMore
            };

        private static Command ListCommand() =>
            new Command(
                name: "list",
                description: "Display a list of counter names and descriptions, grouped by provider.")
            {
                Handler = CommandHandler.Create<IConsole>(List)
            };

        public static int List(IConsole console)
        {
            var profiles = KnownData.GetAllProviders();
            var maxNameLength = profiles.Max(p => p.Name.Length);
            Console.WriteLine("Showing well-known counters only. Specific processes may support additional counters.\n");
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
            return parser.InvokeAsync(args);
        }
    }
}
