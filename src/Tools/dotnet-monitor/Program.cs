using Microsoft.Internal.Common.Commands;
using Microsoft.Tools.Common;
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    class Program
    {
        private static Command CollectCommand() =>
              new Command(
                  name: "collect",
                  description: "Monitor counters in a .NET application and export the result into a file")
              {
                // Handler
                CommandHandler.Create<IConsole, int, int>(new DiagnosticsMonitorCommandHandler().Start),
                // Arguments and Options
                ProcessIdOption(), RefreshIntervalOption()
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
