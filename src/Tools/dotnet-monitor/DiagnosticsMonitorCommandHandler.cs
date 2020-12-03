// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal sealed class DiagnosticsMonitorCommandHandler
    {
        private const string ConfigPrefix = "DotnetMonitor_";
        private const string SettingsFileName = "settings.json";
        private const string ProductFolderName = "dotnet-monitor";

        // Location where shared dotnet-monitor configuration is stored.
        // Windows: "%ProgramData%\dotnet-monitor
        // Other: /etc/dotnet-monitor
        private static readonly string SharedConfigDirectoryPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductFolderName) :
            Path.Combine("/etc", ProductFolderName);

        private static readonly string SharedSettingsPath = Path.Combine(SharedConfigDirectoryPath, SettingsFileName);

        // Location where user's dotnet-monitor configuration is stored.
        // Windows: "%USERPROFILE%\.dotnet-monitor"
        // Other: "%XDG_CONFIG_HOME%/dotnet-monitor" OR "%HOME%/.config/dotnet-monitor"
        private static readonly string UserConfigDirectoryPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "." + ProductFolderName) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductFolderName);

        private static readonly string UserSettingsPath = Path.Combine(UserConfigDirectoryPath, SettingsFileName);

        public async Task<int> Start(CancellationToken token, IConsole console, string[] urls, string[] metricUrls, bool metrics, string diagnosticPort)
        {
            //CONSIDER The console logger uses the standard AddConsole, and therefore disregards IConsole.
            using IHost host = CreateHostBuilder(console, urls, metricUrls, metrics, diagnosticPort).Build();
            await host.RunAsync(token);
            return 0;
        }

        public IHostBuilder CreateHostBuilder(IConsole console, string[] urls, string[] metricUrls, bool metrics, string diagnosticPort)
        {
            return Host.CreateDefaultBuilder()
                .UseContentRoot(AppContext.BaseDirectory) // Use the application root instead of the current directory
                .ConfigureAppConfiguration((IConfigurationBuilder builder) =>
                {
                    //Note these are in precedence order.
                    ConfigureEndpointInfoSource(builder, diagnosticPort);
                    ConfigureMetricsEndpoint(builder, metrics, metricUrls);

                    builder.AddJsonFile(UserSettingsPath, optional: true, reloadOnChange: true);
                    builder.AddJsonFile(SharedSettingsPath, optional: true, reloadOnChange: true);

                    builder.AddKeyPerFile(SharedConfigDirectoryPath, optional: true);
                    builder.AddEnvironmentVariables(ConfigPrefix);
                })
                .ConfigureServices((HostBuilderContext context, IServiceCollection services) =>
                {
                    //TODO Many of these service additions should be done through extension methods
                    services.Configure<DiagnosticPortOptions>(context.Configuration.GetSection(DiagnosticPortOptions.ConfigurationKey));
                    services.AddSingleton<IEndpointInfoSource, FilteredEndpointInfoSource>();
                    services.AddHostedService<FilteredEndpointInfoSourceHostedService>();
                    services.AddSingleton<IDiagnosticServices, DiagnosticServices>();
                    services.ConfigureEgress(context.Configuration);
                    services.ConfigureMetrics(context.Configuration);
                    services.AddSingleton<ExperimentalToolLogger>();
                })
                .ConfigureLogging(builder =>
                {
                    // Always allow the experimental tool message to be logged
                    ExperimentalToolLogger.AddLogFilter(builder);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel((context, options) =>
                    {
                        //Note our priorities for hosting urls don't match the default behavior.
                        //Default Kestrel behavior priority
                        //1) ConfigureKestrel settings
                        //2) Command line arguments (--urls)
                        //3) Environment variables (ASPNETCORE_URLS, then DOTNETCORE_URLS)

                        //Our precedence
                        //1) Environment variables (ASPNETCORE_URLS, DotnetMonitor_Metrics__Endpoints)
                        //2) Command line arguments (these have defaults) --urls, --metricUrls
                        //3) ConfigureKestrel is used for fine control of the server, but honors the first two configurations.

                        string hostingUrl = context.Configuration.GetValue<string>(WebHostDefaults.ServerUrlsKey);
                        if (!string.IsNullOrEmpty(hostingUrl))
                        {
                            urls = ConfigurationHelper.SplitValue(hostingUrl);
                        }

                        var metricsOptions = new MetricsOptions();
                        context.Configuration.Bind(MetricsOptions.ConfigurationKey, metricsOptions);

                        if (metricsOptions.Enabled)
                        {
                            string metricUrlFromConfig = metricsOptions.Endpoints;
                            if (!string.IsNullOrEmpty(metricUrlFromConfig))
                            {
                                metricUrls = ConfigurationHelper.SplitValue(metricUrlFromConfig);
                            }

                            urls = urls.Concat(metricUrls).ToArray();
                        }

                        bool boundListeningPort = false;

                        //Workaround for lack of default certificate. See https://github.com/dotnet/aspnetcore/issues/28120
                        options.Configure(context.Configuration.GetSection("Kestrel")).Load();

                        //By default, we bind to https for sensitive data (such as dumps and traces) and bind http for
                        //non-sensitive data such as metrics. We may be missing a certificate for https binding. We want to continue with the
                        //http binding in that scenario.
                        foreach (BindingAddress url in urls.Select(BindingAddress.Parse))
                        {
                            Action<ListenOptions> configureListenOptions = (listenOptions) =>
                            {
                                if (url.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                                {
                                    listenOptions.UseHttps();
                                }
                            };

                            try
                            {
                                if (url.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                                {
                                    options.ListenLocalhost(url.Port, configureListenOptions);
                                }
                                else if (IPAddress.TryParse(url.Host, out IPAddress ipAddress))
                                {
                                    options.Listen(ipAddress, url.Port, configureListenOptions);
                                }
                                else
                                {
                                    options.ListenAnyIP(url.Port, configureListenOptions);
                                }
                                boundListeningPort = true;
                            }
                            catch (InvalidOperationException e)
                            {
                                //This binding failure is typically due to missing default certificate
                                console.Error.WriteLine($"Unable to bind to {url}. Dotnet-monitor functionality will be limited.");
                                console.Error.WriteLine(e.Message);
                            }
                        }

                        //If we end up not binding any ports, Kestrel defaults to port 5000. Make sure we don't attempt this.
                        if (!boundListeningPort)
                        {
                            throw new InvalidOperationException("Unable to bind any urls.");
                        }
                    })
                    .UseStartup<Startup>();
                });
        }

        private static void ConfigureMetricsEndpoint(IConfigurationBuilder builder, bool enableMetrics, string[] metricEndpoints)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string>
            {
                {ConfigurationHelper.MakeKey(MetricsOptions.ConfigurationKey, nameof(MetricsOptions.Endpoints)), string.Join(';', metricEndpoints)},
                {ConfigurationHelper.MakeKey(MetricsOptions.ConfigurationKey, nameof(MetricsOptions.Enabled)), enableMetrics.ToString()},
                {ConfigurationHelper.MakeKey(MetricsOptions.ConfigurationKey, nameof(MetricsOptions.UpdateIntervalSeconds)), 10.ToString()},
                {ConfigurationHelper.MakeKey(MetricsOptions.ConfigurationKey, nameof(MetricsOptions.MetricCount)), 3.ToString()}
            });
        }

        private static void ConfigureEndpointInfoSource(IConfigurationBuilder builder, string diagnosticPort)
        {
            DiagnosticPortConnectionMode connectionMode = string.IsNullOrEmpty(diagnosticPort) ? DiagnosticPortConnectionMode.Connect : DiagnosticPortConnectionMode.Listen;
            builder.AddInMemoryCollection(new Dictionary<string, string>
            {
                {ConfigurationHelper.MakeKey(DiagnosticPortOptions.ConfigurationKey, nameof(DiagnosticPortOptions.ConnectionMode)), connectionMode.ToString()},
                {ConfigurationHelper.MakeKey(DiagnosticPortOptions.ConfigurationKey, nameof(DiagnosticPortOptions.EndpointName)), diagnosticPort}
            });
        }
    }
}
