// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    /// <summary>
    /// Binds egress configuration information to an <see cref="EgressOptions"/> instance.
    /// </summary>
    internal class EgressConfigureOptions : IConfigureOptions<EgressOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EgressConfigureOptions> _logger;
        private readonly IDictionary<string, EgressFactory> _factories
            = new Dictionary<string, EgressFactory>(StringComparer.OrdinalIgnoreCase);

        public EgressConfigureOptions(
            ILogger<EgressConfigureOptions> logger,
            IConfiguration configuration,
            AzureBlobEgressFactory azureBlobEgressFactory,
            FileSystemEgressFactory fileSystemEgressFactory)
        {
            _configuration = configuration;
            _logger = logger;

            // Register egress providers
            _factories.Add("AzureBlobStorage", azureBlobEgressFactory);
            _factories.Add("FileSystem", fileSystemEgressFactory);
        }

        public void Configure(EgressOptions options)
        {
            IConfigurationSection egressSection = _configuration.GetEgressSection();

            IConfigurationSection propertiesSection = egressSection.GetSection(nameof(EgressOptions.Properties));
            propertiesSection.Bind(options.Properties);

            _logger.LogDebug("Start loading egress providers.");
            IConfigurationSection providersSection = egressSection.GetSection(nameof(EgressOptions.Providers));
            foreach (var providerSection in providersSection.GetChildren())
            {
                string providerName = providerSection.Key;

                using var providerNameScope = _logger.BeginScope(new Dictionary<string, string>() { { "ProviderName", providerName } });

                CommonEgressProviderOptions commonOptions = new CommonEgressProviderOptions();
                providerSection.Bind(commonOptions);

                EgressProviderValidation validation = new EgressProviderValidation(providerName, _logger);
                if (!validation.TryValidate(commonOptions))
                {
                    _logger.LogWarning("Provider '{0}': Skipped: Invalid options.", providerName);
                }

                string providerType = commonOptions.Type;
                using var providerTypeScope = _logger.BeginScope(new Dictionary<string, string>() { { "ProviderType", providerType } });

                if (!_factories.TryGetValue(providerType, out EgressFactory factory))
                {
                    _logger.LogWarning("Provider '{0}': Skipped: Type '{1}' is not supported.", providerName, providerType);
                    continue;
                }

                if (!factory.TryCreate(providerName, providerSection, options.Properties, out ConfiguredEgressProvider provider))
                {
                    _logger.LogWarning("Provider '{0}': Skipped: Invalid options.", providerName);
                    continue;
                }

                options.Providers.Add(providerName, provider);

                _logger.LogInformation("Added egress provider '{0}'.", providerName);
            }
            _logger.LogDebug("End loading egress providers.");
        }

        /// <summary>
        /// Configuration options to all egress providers.
        /// </summary>
        private class CommonEgressProviderOptions
        {
            /// <summary>
            /// The type of the egress provider.
            /// </summary>
            [Required]
            public string Type { get; set; }
        }
    }
}
