using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Internal.Common.Commands;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Logs
{
    internal class Program
    {
        private static Command MonitorCommand() =>
            new Command(
                "monitor",
                "Start monitoring a .NET application",
                new Option[] { ProcessIdOption() },
                argument: LogFilterList(),
                handler: CommandHandler.Create<CancellationToken, List<string>, IConsole, int>(
                    (cancellationToken, logFilterList, console, processId) =>
                    new HostBuilder()
                        .ConfigureAppConfiguration((hostingContext, config) =>
                        {
                            config.AddJsonFile("appsettings.json", optional: true);
                        })
                        .ConfigureLogging((hostingContext, logging) =>
                        {
                            logging.AddConfiguration(hostingContext.Configuration);
                        })
                        .ConfigureServices((hostBuilder, services) =>
                        {
                            services
                                .Configure<ConsoleLifetimeOptions>(options =>
                                {
                                    options.SuppressStatusMessages = true;
                                })
                                .AddHostedService<LogViewerService>()
                                .Configure<LogViewerServiceOptions>(options =>
                                {
                                    options.ProcessId = processId;
                                });
                        })
                        .Build()
                        .RunAsync(cancellationToken))
                );

        private static Option ProcessIdOption() =>
            new Option(
                new[] { "-p", "--process-id" },
                "The ID of the process that will be monitored.",
                new Argument<int> { Name = "pid" });

        private static Argument LogFilterList() =>
            new Argument<List<string>>
            {
                Name = "logfilter_list",
                Description = "A space separated list of log filters.",
                Arity = ArgumentArity.ZeroOrMore
            };

        private static Command ProcessStatusCommand() =>
            new Command(
                "ps",
                "Display a list of dotnet processes that can be monitored.",
                new Option[] { },
                handler: CommandHandler.Create<IConsole>(ProcessStatusCommandHandler.PrintProcessStatus));
        private static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .AddCommand(MonitorCommand())
                .AddCommand(ProcessStatusCommand())
                .UseDefaults()
                .Build();
            return parser.InvokeAsync(args);
        }
    }
}
