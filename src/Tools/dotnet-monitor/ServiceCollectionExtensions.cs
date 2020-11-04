// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureEgress(this IServiceCollection services, IConfiguration configuration)
        {
            // Register change token for EgressOptions binding
            services.AddSingleton((IOptionsChangeTokenSource<EgressOptions>)new ConfigurationChangeTokenSource<EgressOptions>(GetEgressSection(configuration)));

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

        private static IConfigurationSection GetEgressSection(IConfiguration configuration)
        {
            return configuration.GetSection(EgressOptions.ConfigurationKey);
        }

        private class EgressConfigureOptions : IConfigureOptions<EgressOptions>
        {
            private readonly IConfiguration _configuration;
            private readonly ILogger<EgressConfigureOptions> _logger;
            private readonly IDictionary<string, EgressFactory> _factories
                = new Dictionary<string, EgressFactory>(StringComparer.OrdinalIgnoreCase);

            public EgressConfigureOptions(
                ILogger<EgressConfigureOptions> logger,
                IConfiguration configuration)
            {
                _configuration = configuration;
                _logger = logger;

                // Register egress providers
                _factories.Add("AzureBlobStorage", new AzureBlobEgressFactory());
                _factories.Add("FileSystem", new FileSystemEgressFactory());
            }

            public void Configure(EgressOptions options)
            {
                IConfigurationSection egressSection = GetEgressSection(_configuration);

                IConfigurationSection propertiesSection = egressSection.GetSection(nameof(EgressOptions.Properties));
                propertiesSection.Bind(options.Properties);

                IConfigurationSection providersSection = egressSection.GetSection(nameof(EgressOptions.Providers));
                foreach (var providerSection in providersSection.GetChildren())
                {
                    string providerName = providerSection.Key;

                    if (!TryGetProviderType(providerSection, out string providerType))
                    {
                        _logger.LogWarning("Egress provider '{0}' does not have a 'type' setting.", providerName);
                        continue;
                    }
                    
                    if (!_factories.TryGetValue(providerType, out EgressFactory factory))
                    {
                        _logger.LogWarning("Provider type '{0}' on provider '{1}' is not supported.", providerType, providerName);
                        continue;
                    }
                    
                    if (!factory.TryCreate(providerName, providerSection, options.Properties, out ConfiguredEgressProvider provider))
                    {
                        _logger.LogWarning("Unable to create egress provider '{0}' due to invalid options.", providerName);
                        continue;
                    }

                    options.Providers.Add(providerName, provider);
                }
            }

            private static bool TryGetProviderType(IConfigurationSection section, out string providerTypeName)
            {
                try
                {
                    providerTypeName = section.GetValue<string>("type", defaultValue: null);
                }
                catch (InvalidOperationException)
                {
                    providerTypeName = null;
                    return false;
                }

                return !string.IsNullOrEmpty(providerTypeName);
            }
        }
    }
}
