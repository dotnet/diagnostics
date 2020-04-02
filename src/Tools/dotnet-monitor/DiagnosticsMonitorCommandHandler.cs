// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Diagnostics.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
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
                    builder.AddKeyPerFile(keyFileConfig.FullName, optional: true);
                }
            }

            ConfigureNames(builder);

            IConfigurationRoot config = builder.Build();
            services.AddSingleton<IConfiguration>(config);

            //Specialized logger for diagnostic output from the service itself rather than as a sink for the data
            services.AddSingleton<ILogger<DiagnosticsMonitor>>((sp) => new ConsoleLoggerAdapter(console));

            if (sink.HasFlag(SinkType.Console))
            {
                services.AddSingleton<IMetricsLogger, ConsoleMetricsLogger>();
            }

            services.AddLogging(builder =>
                {
                    if (sink.HasFlag(SinkType.Console))
                    {
                        builder.AddConsole();
                    }
                });
            services.Configure<ContextConfiguration>(config);

            //TODO Many of these service additions should be done through extension methods
            services.AddSingleton<IDiagnosticServices, DiagnosticServices>();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IDiagnosticServices diagServices = serviceProvider.GetService<IDiagnosticServices>();
            using IWebHost host = Microsoft.Diagnostics.Monitoring.RestServer.Program.CreateWebHostBuilder(diagServices).Build();
            await host.RunAsync(token);
            return 0;
        }

        private void ConfigureNames(IConfigurationBuilder builder)
        {
            string hostName = Environment.GetEnvironmentVariable("HOSTNAME");
            if (string.IsNullOrEmpty(hostName))
            {
                hostName = Environment.MachineName;
            }
            string namespaceName = null;
            try
            {
                string nsFile = @"/var/run/secrets/kubernetes.io/serviceaccount/namespace";
                if (File.Exists(nsFile))
                {
                    namespaceName = File.ReadAllText(nsFile);
                }
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(namespaceName))
            {
                namespaceName = "default";
            }

            builder.AddInMemoryCollection(new Dictionary<string, string> { { DiagnosticsMonitor.NamespaceName, namespaceName }, { DiagnosticsMonitor.NodeName, hostName } });

        }
    }
}
