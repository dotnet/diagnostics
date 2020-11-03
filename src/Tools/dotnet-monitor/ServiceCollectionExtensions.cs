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
            // The options are manually created due to how the Endpoints property
            // hold concrete implementations that are based on the 'type' property
            // for each endpoint entry.
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
            private readonly IDictionary<string, EgressProvider> _providers
                = new Dictionary<string, EgressProvider>(StringComparer.OrdinalIgnoreCase);

            public EgressConfigureOptions(
                ILogger<EgressConfigureOptions> logger,
                IConfiguration configuration)
            {
                _configuration = configuration;
                _logger = logger;

                // Register egress providers
                _providers.Add("AzureBlobStorage", new AzureBlobEgressProvider());
                _providers.Add("FileSystem", new FileSystemEgressProvider());
            }

            public void Configure(EgressOptions options)
            {
                IConfigurationSection egressSection = GetEgressSection(_configuration);

                IConfigurationSection propertiesSection = egressSection.GetSection(nameof(EgressOptions.Properties));
                propertiesSection.Bind(options.Properties);

                IConfigurationSection endpointsSection = egressSection.GetSection(nameof(EgressOptions.Endpoints));
                foreach (var endpointSection in endpointsSection.GetChildren())
                {
                    string endpointName = endpointSection.Key;

                    if (!TryGetEgressType(endpointSection, out string egressTypeName))
                    {
                        _logger.LogWarning("Egress endpoint '{0}' does not have a 'type' setting.", endpointName);
                        continue;
                    }
                    
                    if (!_providers.TryGetValue(egressTypeName, out EgressProvider provider))
                    {
                        _logger.LogWarning("Egress type '{0}' on endpoint '{1}' is not supported.", egressTypeName, endpointName);
                        continue;
                    }
                    
                    if (!provider.TryParse(endpointName, endpointSection, options.Properties, out ConfiguredEgressEndpoint endpoint))
                    {
                        _logger.LogWarning("Unable to create egress endpoint '{0}' due to invalid options.", endpointName);
                        continue;
                    }

                    options.Endpoints.Add(endpointName, endpoint);
                }
            }

            private static bool TryGetEgressType(IConfigurationSection section, out string endpointTypeName)
            {
                try
                {
                    endpointTypeName = section.GetValue<string>("type", defaultValue: null);
                }
                catch (InvalidOperationException)
                {
                    endpointTypeName = null;
                    return false;
                }

                return !string.IsNullOrEmpty(endpointTypeName);
            }
        }
    }
}
