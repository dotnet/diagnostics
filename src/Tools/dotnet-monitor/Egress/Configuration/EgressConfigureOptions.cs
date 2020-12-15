// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress.Configuration
{
    /* 
     * == Egress Configuration Design ==
     * - Each type of egress is called an egress type. The following are the defined egress types:
     *   - AzureBlobStorage: Allows egressing stream data to a blob in Azure blob storage.
     *   - FileSystem: Allows egressing stream data to the file system.
     * - An egress type in combination with its well defined set of options is called a egress provider.
     *   Each egress provider is named in the egress configuration in order to identify individual providers.
     * - All egress configuration information is found in the root Egress configuration section. This section
     *   has two immediate subsections:
     *   - Providers: a mapping of egress provider names to egress provider options + the egress type.
     *     - Each provider must have a 'type' field, which must have a value that is one of the egress types.
     *       This field informs the egress configuration the type of egress provider that should be constructed
     *       with the remaining options.
     *     - If a provider's options fail validation, the failure is reported and the provider will not be
     *       available to be used as a means of egress.
     *   - Properties: a mapping of named values, typically for storing secrets (account keys, SAS, etc) by name.
     */

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
            ILoggerFactory loggerFactory,
            IConfiguration configuration)
        {
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger<EgressConfigureOptions>();

            // Register egress providers
            _factories.Add("AzureBlobStorage", new AzureBlobEgressFactory(loggerFactory));
            _factories.Add("FileSystem", new FileSystemEgressFactory(loggerFactory));
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

                KeyValueLogScope providerNameScope = new KeyValueLogScope();
                providerNameScope.Values.Add("EgressProviderName", providerName);
                using var providerNameRegistration = _logger.BeginScope(providerNameScope);

                CommonEgressProviderOptions commonOptions = new CommonEgressProviderOptions();
                providerSection.Bind(commonOptions);

                EgressProviderValidation validation = new EgressProviderValidation(providerName, _logger);
                if (!validation.TryValidate(commonOptions))
                {
                    _logger.LogWarning("Provider '{0}': Skipped: Invalid options.", providerName);
                }

                string providerType = commonOptions.Type;
                KeyValueLogScope providerTypeScope = new KeyValueLogScope();
                providerTypeScope.Values.Add("EgressProviderType", providerType);
                using var providerTypeRegistration = _logger.BeginScope(providerTypeScope);

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

                _logger.LogDebug("Added egress provider '{0}'.", providerName);
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
