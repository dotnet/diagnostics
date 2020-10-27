// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.Egress;
using Microsoft.Diagnostics.Monitoring.Egress.AzureStorage;
using Microsoft.Diagnostics.Monitoring.Egress.FileSystem;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureEgress(this IServiceCollection services, IConfiguration configuration)
        {
            // Configure AzureStorageOptions to bind to the AzureStorage configuration key.
            services.Configure<AzureStorageOptions>(configuration.GetSection(AzureStorageOptions.ConfigurationKey));

            IConfigurationSection egressSection = configuration.GetSection(EgressOptions.ConfigurationKey);

            // Register change token for EgressOptions binding
            services.AddSingleton((IOptionsChangeTokenSource<EgressOptions>)new ConfigurationChangeTokenSource<EgressOptions>(egressSection));

            // Configure EgressOptions to bind to the Egress configuration key.
            // The options are manually created due to how the Endpoints property
            // hold concrete implementations that are based on the 'type' property
            // for each endpoint entry.
            services.Configure<EgressOptions>(egress => {
                IConfigurationSection endpointsSection = egressSection
                    .GetSection(nameof(EgressOptions.Endpoints));

                foreach (var child in endpointsSection.GetChildren())
                {
                    if (TryCreateSettings(child, out var settings))
                    {
                        egress.Endpoints.Add(child.Key, settings);
                    }
                }
            });

            // Register IEgressService implementation that provides egressing
            // of artifacts for the REST server.
            services.AddSingleton<IEgressService, EgressService>();

            return services;
        }

        private static bool TryCreateSettings(IConfigurationSection section, out EgressEndpointOptions settings)
        {
            EndpointType endpointType;
            try
            {
                endpointType = section.GetValue("type", EndpointType.Unknown);
            }
            catch (InvalidOperationException)
            {
                settings = null;
                return false;
            }

            switch (endpointType)
            {
                case EndpointType.AzureBlobStorage:
                    settings = section.Get<AzureBlobEgressEndpointOptions>();
                    return true;
                case EndpointType.FileSystem:
                    settings = section.Get<FileSystemEgressEndpointOptions>();
                    return true;
            }

            settings = null;
            return false;
        }

        private enum EndpointType
        {
            Unknown = 0,
            FileSystem,
            AzureBlobStorage
        }
    }
}
