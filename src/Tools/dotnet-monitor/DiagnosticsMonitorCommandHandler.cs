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
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal sealed class DiagnosticsMonitorCommandHandler
    {
        public async Task<int> Start(CancellationToken token, IConsole console, string[] urls, bool metrics)
        {
            //CONSIDER The console logger uses the standard AddConsole, and therefore disregards IConsole.
            using IWebHost host = CreateWebHostBuilder(console, urls, metrics).Build();
            await host.RunAsync(token);
            return 0;
        }

        public IWebHostBuilder CreateWebHostBuilder(IConsole console, string[] urls, bool metrics)
        {
            string metricsEndpoint = null;
            if (metrics)
            {
                metricsEndpoint = GetMetricsEndpoint();
                urls = new List<string>(urls) { metricsEndpoint }.ToArray();
            }

            IWebHostBuilder builder = WebHost.CreateDefaultBuilder()
                .ConfigureAppConfiguration((IConfigurationBuilder builder) =>
                {
                    if (metrics)
                    {
                        ConfigureMetricsEndpoint(builder, metricsEndpoint);
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

        private string GetMetricsEndpoint()
        {
            string endpoint = "http://localhost:52325";
            if (RuntimeInfo.IsInDockerContainer)
            {
                //Necessary for prometheus scraping
                endpoint = "http://*:52325";
            }
            return endpoint;
        }

        private static void ConfigureMetricsEndpoint(IConfigurationBuilder builder, string endpoint)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string>
            {
                {MakeKey(nameof(PrometheusConfiguration), nameof(PrometheusConfiguration.Endpoint)), endpoint},
                {MakeKey(nameof(PrometheusConfiguration), nameof(PrometheusConfiguration.Enabled)), true.ToString()},
                {MakeKey(nameof(PrometheusConfiguration), nameof(PrometheusConfiguration.UpdateIntervalSeconds)), 30.ToString()}
            });
        }

        private static string MakeKey(string parent, string child)
        {
            return FormattableString.Invariant($"{parent}:{child}");
        }
    }
}
