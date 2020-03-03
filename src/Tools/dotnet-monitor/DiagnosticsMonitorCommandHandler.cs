using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.LogAnalytics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
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

        public async Task<int> Start(CancellationToken token, IConsole console, int processId, int refreshInterval, SinkType sink, IEnumerable<FileInfo> jsonConfigs, IEnumerable<FileInfo> keyFileConfigs)
        {
            //CONSIDER The console sink uses the standard AddConsole, and therefore disregards IConsole.

            ServiceCollection services = new ServiceCollection();
            ConfigurationBuilder builder = new ConfigurationBuilder();

            if (jsonConfigs != null)
            {
                foreach (FileInfo jsonFile in jsonConfigs)
                {
                    builder.SetBasePath(jsonFile.DirectoryName).AddJsonFile(jsonFile.Name, optional: true);
                }
            }
            if (keyFileConfigs != null)
            {
                foreach (FileInfo keyFileConfig in keyFileConfigs)
                {
                    console.Out.WriteLine(keyFileConfig.FullName);
                    builder.AddKeyPerFile(keyFileConfig.FullName, optional: true);
                }
            }
            builder.AddInMemoryCollection(new Dictionary<string, string> { { "Namespace", "default" }, { "Node", Environment.MachineName } });

            IConfigurationRoot config = builder.Build();

            services.AddSingleton<IConfiguration>(config);

            //Specialized logger for diagnostic output from the service itself rather than as a sink for the data
            services.AddSingleton<ILogger<DiagnosticsMonitor>>((sp) => new ConsoleLoggerAdapter(console));

            if (sink.HasFlag(SinkType.console))
            {
                services.AddSingleton<IMetricsLogger, ConsoleMetricsLogger>();
            }
            if (sink.HasFlag(SinkType.logAnalytics))
            {
                services.AddSingleton<IMetricsLogger, MetricsLogger>();
            }

            services.AddLogging(builder =>
                {
                    if (sink.HasFlag(SinkType.console))
                    {
                        builder.AddConsole();
                    }
                    if (sink.HasFlag(SinkType.logAnalytics))
                    {
                        builder.AddProvider(new LogAnalyticsLoggerProvider());
                    }
                });
            services.Configure<ContextConfiguration>(config);
            if (sink.HasFlag(SinkType.logAnalytics))
            {
                services.Configure<MetricsConfiguration>(config);
                services.Configure<ResourceConfiguration>(config);
            }

            DiagnosticsMonitor monitor = new DiagnosticsMonitor(services.BuildServiceProvider(), new MonitoringSourceConfiguration(refreshInterval));

            await monitor.ProcessEvents(processId, token);

            return 0;
        }
    }
}
