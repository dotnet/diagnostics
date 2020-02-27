using Microsoft.Diagnostics.Monitoring;
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
        private sealed class ConsoleLoggerAdapter : ILogger
        {
            private IConsole _console;

            private sealed class EmptyScope : IDisposable
            {
                public static EmptyScope Instance { get; } = new EmptyScope();
               
                public void Dispose() {}
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
            DiagnosticsMonitor monitor = new DiagnosticsMonitor(new ContextConfiguration(), new MonitoringSourceConfiguration(refreshInterval), new[] { new ConsoleSinkConfiguration() }, new ConsoleLoggerAdapter(console));

            await monitor.ProcessEvents(processId, token);

            return 0;
        }
    }
}
