using Microsoft.Diagnostics.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal sealed class DiagnosticsMonitorCommandHandler
    {
        private sealed class ConsoleLoggerAdapter : ILogger<DiagnosticsMonitor>
        {
            private readonly IConsole _console;

            private sealed class EmptyScope : IDisposable
            {
                public static EmptyScope Instance { get; } = new EmptyScope();

                public void Dispose() { }
            }

            public ConsoleLoggerAdapter(IConsole console)
            {
                _console = console;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return EmptyScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                _console.Out.WriteLine((formatter != null) ? formatter.Invoke(state, exception) : state?.ToString());
            }
        }

        public async Task<int> Start(CancellationToken token, IConsole console, int processId, int refreshInterval, SinkType sinkType)
        {
            //CONSIDER The console sink uses the standard AddConsole, and therefore disregards IConsole.

            ServiceCollection services = new ServiceCollection();

            //Specialized logger for diagnostic output from the service itself rather than as a sink for the data
            services.AddSingleton<ILogger<DiagnosticsMonitor>>((sp) => new ConsoleLoggerAdapter(console));

            services.AddSingleton<IMetricsLogger, ConsoleMetricsLogger>();
            services.AddLogging(builder => builder.AddConsole());
            services.Configure<ContextConfiguration>( contextConfig =>
            {
                contextConfig.Namespace = "default";
                contextConfig.Node = Environment.MachineName;
            });

            DiagnosticsMonitor monitor = new DiagnosticsMonitor(services.BuildServiceProvider(), new MonitoringSourceConfiguration(refreshInterval));

            await monitor.ProcessEvents(processId, token);

            return 0;
        }
    }
}
