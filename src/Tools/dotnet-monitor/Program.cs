using Microsoft.Internal.Common.Commands;
using Microsoft.Tools.Common;
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal enum SinkType
    {
        console,
        logAnalytics,
    }
    
    class Program
    {
        private static Command CollectCommand() =>
              new Command(
                  name: "collect",
                  description: "Monitor logs and metrics in a .NET application send the results to a chosen destination.")
              {
                // Handler
                CommandHandler.Create<CancellationToken, IConsole, int, int, SinkType>(new DiagnosticsMonitorCommandHandler().Start),
                // Arguments and Options
                ProcessIdOption(), RefreshIntervalOption(), SinkOption()
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
                description: "The number of seconds to delay between updating the counters.")
            {
                Argument = new Argument<int>(name: "refresh-interval", defaultValue: 1)
            };

        private static Option SinkOption() =>
            new Option(
                alias: "--sink",
                description: "Where to send the data")
            {
                Argument = new Argument<SinkType>(name: "sink", defaultValue: SinkType.console)
            };


        public static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                            .AddCommand(CollectCommand())
                            .AddCommand(ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that can be monitored"))
                            .UseDefaults()
                            .Build();
            return parser.InvokeAsync(args);
        }
    }
}
