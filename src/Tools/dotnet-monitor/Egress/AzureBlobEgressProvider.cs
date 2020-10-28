// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.Egress.AzureStorage;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal class AzureBlobEgressProvider : EgressProvider
    {
        private readonly IOptionsMonitor<AzureStorageOptions> _azureStorageOptions;

        public AzureBlobEgressProvider(IOptionsMonitor<AzureStorageOptions> azureStorageOptions)
        {
            _azureStorageOptions = azureStorageOptions;
        }

        public override bool TryParse(string endpointName, IConfigurationSection config, out ConfiguredEgressEndpoint endpoint)
        {
            var optionsTemplate = config.Get<AzureBlobEgressEndpointOptions>();

            // TODO: Validate options

            endpoint = new Endpoint(endpointName, optionsTemplate, _azureStorageOptions);
            return true;
        }

        private class Endpoint : ConfiguredEgressEndpoint
        {
            private readonly IOptionsMonitor<AzureStorageOptions> _azureStorageOptions;
            private readonly string _endpointName;
            private readonly AzureBlobEgressEndpointOptions _optionsTemplate;

            public Endpoint(
                string endpointName,
                AzureBlobEgressEndpointOptions optionsTemplate,
                IOptionsMonitor<AzureStorageOptions> azureStorageOptions)
            {
                _azureStorageOptions = azureStorageOptions;
                _endpointName = endpointName;
                _optionsTemplate = optionsTemplate;
            }

            public override async Task<EgressResult> EgressAsync(
                Func<Stream, CancellationToken, Task> action,
                string fileName,
                string contentType,
                IEndpointInfo source,
                CancellationToken token)
            {
                // TODO: Add metadata based on source
                var streamOptions = new AzureBlobEgressStreamOptions();
                streamOptions.ContentType = contentType;

                var endpoint = new AzureBlobEgressEndpoint(CreateEndpointOptions());
                string blobUri = await endpoint.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("uri", blobUri);
            }

            private AzureBlobEgressEndpointOptions CreateEndpointOptions()
            {
                var endpointOptions = new AzureBlobEgressEndpointOptions(_optionsTemplate);

                // If SAS token is not specified, use the one from the SAS token configuration that has
                // a name that matches the endpoint name.
                if (string.IsNullOrEmpty(endpointOptions.SharedAccessSignature) &&
                    _azureStorageOptions.CurrentValue.SharedAccessSignatures.TryGetValue(_endpointName, out string sasToken))
                {
                    endpointOptions.SharedAccessSignature = sasToken;
                }

                return endpointOptions;
            }
        }
    }
}
