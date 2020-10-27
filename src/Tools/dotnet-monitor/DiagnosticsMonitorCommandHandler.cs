// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal sealed class DiagnosticsMonitorCommandHandler
    {
        private const string ConfigPrefix = "DotnetMonitor_";
        private const string ConfigPath = "/etc/dotnet-monitor";

        public async Task<int> Start(CancellationToken token, IConsole console, string[] urls, string[] metricUrls, bool metrics, string diagnosticPort)
        {
            //CONSIDER The console logger uses the standard AddConsole, and therefore disregards IConsole.
            using IWebHost host = CreateWebHostBuilder(console, urls, metricUrls, metrics, diagnosticPort).Build();
            await host.RunAsync(token);
            return 0;
        }

        public IWebHostBuilder CreateWebHostBuilder(IConsole console, string[] urls, string[] metricUrls, bool metrics, string diagnosticPort)
        {
            if (metrics)
            {
                urls = urls.Concat(metricUrls).ToArray();
            }

            IWebHostBuilder builder = WebHost.CreateDefaultBuilder()
                .ConfigureAppConfiguration((IConfigurationBuilder builder) =>
                {
                    ConfigureEndpointInfoSource(builder, diagnosticPort);
                    if (metrics)
                    {
                        //Note these are in precedence order.
                        ConfigureMetricsEndpoint(builder, metricUrls);
                    }

                    builder.AddKeyPerFile(ConfigPath, optional: true);

                    builder.AddEnvironmentVariables(ConfigPrefix);
                })
                .ConfigureServices((WebHostBuilderContext context, IServiceCollection services) =>
                {
                    //TODO Many of these service additions should be done through extension methods
                    services.Configure<DiagnosticPortOptions>(context.Configuration.GetSection(DiagnosticPortOptions.ConfigurationKey));
                    services.AddSingleton<IEndpointInfoSource, FilteredEndpointInfoSource>();
                    services.AddHostedService<FilteredEndpointInfoSourceHostedService>();
                    services.AddSingleton<IDiagnosticServices, DiagnosticServices>();
                    if (metrics)
                    {
                        services.Configure<MetricsOptions>(context.Configuration.GetSection(MetricsOptions.ConfigurationKey));
                    }
                })
                .UseUrls(urls)
                .UseStartup<Startup>();

            return builder;
        }

        private static void ConfigureMetricsEndpoint(IConfigurationBuilder builder, string[] metricEndpoints)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string>
            {
                {MakeKey(MetricsOptions.ConfigurationKey, nameof(MetricsOptions.Endpoints)), string.Join(';', metricEndpoints)},
                {MakeKey(MetricsOptions.ConfigurationKey, nameof(MetricsOptions.Enabled)), true.ToString()},
                {MakeKey(MetricsOptions.ConfigurationKey, nameof(MetricsOptions.UpdateIntervalSeconds)), 10.ToString()},
                {MakeKey(MetricsOptions.ConfigurationKey, nameof(MetricsOptions.MetricCount)), 3.ToString()}
            });
        }

        private static void ConfigureEndpointInfoSource(IConfigurationBuilder builder, string diagnosticPort)
        {
            DiagnosticPortConnectionMode connectionMode = string.IsNullOrEmpty(diagnosticPort) ? DiagnosticPortConnectionMode.Connect : DiagnosticPortConnectionMode.Listen;
            builder.AddInMemoryCollection(new Dictionary<string, string>
            {
                {MakeKey(DiagnosticPortOptions.ConfigurationKey, nameof(DiagnosticPortOptions.ConnectionMode)), connectionMode.ToString()},
                {MakeKey(DiagnosticPortOptions.ConfigurationKey, nameof(DiagnosticPortOptions.EndpointName)), diagnosticPort}
            });
        }

        private static string MakeKey(string parent, string child)
        {
            return FormattableString.Invariant($"{parent}:{child}");
        }
    }
}
