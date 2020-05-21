// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal sealed class DiagnosticsMonitorCommandHandler
    {
        private const string ConfigPrefix = "DotnetMonitor_";
        private const string ConfigPath = "/etc/dotnet-monitor";

        public async Task<int> Start(CancellationToken token, IConsole console, string[] urls, string[] metricUrls, bool metrics)
        {
            //CONSIDER The console logger uses the standard AddConsole, and therefore disregards IConsole.
            using IWebHost host = CreateWebHostBuilder(console, urls, metricUrls, metrics).Build();
            await host.RunAsync(token);
            return 0;
        }

        public IWebHostBuilder CreateWebHostBuilder(IConsole console, string[] urls, string[] metricUrls, bool metrics)
        {
            if (metrics)
            {
                urls = urls.Concat(metricUrls).ToArray();
            }

            IWebHostBuilder builder = WebHost.CreateDefaultBuilder()
                .ConfigureAppConfiguration((IConfigurationBuilder builder) =>
                {
                    if (metrics)
                    {
                        //Note these are in precedence order.
                        ConfigureMetricsEndpoint(builder, metricUrls);
                        builder.AddKeyPerFile(ConfigPath, optional: true);
                        builder.AddEnvironmentVariables(ConfigPrefix);
                    }
                })
                .ConfigureServices((WebHostBuilderContext context, IServiceCollection services) =>
                {
                    //TODO Many of these service additions should be done through extension methods
                    services.AddSingleton<IDiagnosticServices, DiagnosticServices>();
                    if (metrics)
                    {
                        services.Configure<PrometheusConfiguration>(context.Configuration.GetSection(nameof(PrometheusConfiguration)));
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
                {MakeKey(nameof(PrometheusConfiguration), nameof(PrometheusConfiguration.Endpoints)), string.Join(';',metricEndpoints)},
                {MakeKey(nameof(PrometheusConfiguration), nameof(PrometheusConfiguration.Enabled)), true.ToString()},
                {MakeKey(nameof(PrometheusConfiguration), nameof(PrometheusConfiguration.UpdateIntervalSeconds)), 10.ToString()},
                {MakeKey(nameof(PrometheusConfiguration), nameof(PrometheusConfiguration.MetricCount)), 3.ToString()}
            });
        }

        private static string MakeKey(string parent, string child)
        {
            return FormattableString.Invariant($"{parent}:{child}");
        }
    }
}
