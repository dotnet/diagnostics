// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Diagnostics.Tools.RuntimeClient;

namespace Microsoft.Diagnostics.Tools.Counters
{
    internal class Program
    {
        private static Command MonitorCommand() =>
            new Command(
                "monitor", 
                "Start monitoring a .NET application", 
                new Option[] { ProcessIdOption(), RefreshIntervalOption() },
                argument: CounterList(),
                handler: CommandHandler.Create<CancellationToken, List<string>, IConsole, int, int>(new CounterMonitor().Monitor));

        private static Option ProcessIdOption() =>
            new Option(
                new[] { "-p", "--process-id" }, 
                "The ID of the process that will be monitored.",
                new Argument<int> { Name = "pid" });

        private static Option RefreshIntervalOption() =>
            new Option(
                new[] { "--refresh-interval" }, 
                "The number of seconds to delay between updating the displayed counters.",
                new Argument<int>(defaultValue: 1) { Name = "refresh-interval" });

        private static Argument CounterList() =>
            new Argument<List<string>> {
                Name = "counter_list",
                Description = @"A space separated list of counters. Counters can be specified provider_name[:counter_name].
                If the provider_name is used without a qualifying counter_name then all counters will be shown. To discover 
                provider and counter names, use the list command.
                .",
                Arity = ArgumentArity.ZeroOrMore
            };

        private static Command ListCommand() =>
            new Command(
                "list", 
                "Display a list of counter names and descriptions, grouped by provider.", 
                new Option[] { },
                handler: CommandHandler.Create<IConsole>(List));

        private static Command ListProcessesCommand() =>
            new Command(
                "list-processes",
                "Display a list of dotnet processes that can be monitored.",
                new Option[] { },
                handler: CommandHandler.Create<IConsole>(ListProcesses));

        private static int ListProcesses(IConsole console)
        {
            var processes = EventPipeClient.ListAvailablePorts()
                .Select(GetProcess)
                .Where(process => process != null)
                .OrderBy(process => process.ProcessName)
                .ThenBy(process => process.Id);

            foreach (var process in processes)
            {
                try
                {
                    console.Out.WriteLine($"{process.Id, 10} {process.ProcessName, -10} {process.MainModule.FileName}");
                }
                catch (Exception)
                {
                    console.Out.WriteLine($"{process.Id, 10} {process.ProcessName, -10}");
                }
            }

            return 0;
        }

        private static System.Diagnostics.Process GetProcess(int processId)
        {
            try
            {
                return System.Diagnostics.Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

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
                .AddCommand(ListCommand())
                .AddCommand(ListProcessesCommand())
                .UseDefaults()
                .Build();
            return parser.InvokeAsync(args);
        }
    }
}
