// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Diagnostics.Tools.Monitor.Egress;
using Microsoft.Diagnostics.Tools.Monitor.Egress.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureMetrics(this IServiceCollection services, IConfiguration configuration)
        {
            return ConfigureOptions<MetricsOptions>(services, configuration, MetricsOptions.ConfigurationKey);
        }

        public static IServiceCollection ConfigureApiKeyConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            return ConfigureOptions<ApiAuthenticationOptions>(services, configuration, ApiAuthenticationOptions.ConfigurationKey);
        }

        private static IServiceCollection ConfigureOptions<T>(IServiceCollection services, IConfiguration configuration, string key) where T : class
        {
            return services.Configure<T>(configuration.GetSection(key));
        }

        public static IServiceCollection ConfigureEgress(this IServiceCollection services, IConfiguration configuration)
        {
            // Register change token for EgressOptions binding
            services.AddSingleton<IOptionsChangeTokenSource<EgressOptions>>(new ConfigurationChangeTokenSource<EgressOptions>(configuration.GetEgressSection()));

            // Configure EgressOptions to bind to the Egress configuration key.
            // The options are manually created due to how the Providers property
            // holds concrete implementations that are based on the 'type' property
            // for each provider entry.
            services.AddSingleton<IConfigureOptions<EgressOptions>, EgressConfigureOptions>();

            // Register IEgressService implementation that provides egressing
            // of artifacts for the REST server.
            services.AddSingleton<IEgressService, EgressService>();

            return services;
        }
    }
}
