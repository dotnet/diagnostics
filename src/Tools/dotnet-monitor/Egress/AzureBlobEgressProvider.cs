// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.Egress.AzureStorage;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal class AzureBlobEgressProvider : EgressProvider
    {
        public override bool TryParse(
            string endpointName,
            IConfigurationSection endpointSection,
            Dictionary<string, string> egressProperties,
            out ConfiguredEgressEndpoint endpoint)
        {
            var endpointOptions = endpointSection.Get<AzureBlobEgressEndpointOptions>();

            // TODO: Validate options

            if (string.IsNullOrEmpty(endpointOptions.AccountKey) &&
                    !string.IsNullOrEmpty(endpointOptions.AccountKeyName))
            {
                if (egressProperties.TryGetValue(endpointOptions.AccountKeyName, out string key))
                {
                    endpointOptions.AccountKey = key;
                }
            }


            if (string.IsNullOrEmpty(endpointOptions.SharedAccessSignature) &&
                !string.IsNullOrEmpty(endpointOptions.SharedAccessSignatureName))
            {
                if (egressProperties.TryGetValue(endpointOptions.SharedAccessSignatureName, out string signature))
                {
                    endpointOptions.SharedAccessSignature = signature;
                }
            }

            endpoint = new Endpoint(endpointName, endpointOptions);
            return true;
        }

        private class Endpoint : ConfiguredEgressEndpoint
        {
            private readonly string _endpointName;
            private readonly AzureBlobEgressEndpointOptions _endpointOptions;

            public Endpoint(
                string endpointName,
                AzureBlobEgressEndpointOptions endpointOptions)
            {
                _endpointName = endpointName;
                _endpointOptions = endpointOptions;
            }

            public override async Task<EgressResult> EgressAsync(
                Func<CancellationToken, Task<Stream>> action,
                string fileName,
                string contentType,
                IEndpointInfo source,
                CancellationToken token)
            {
                // TODO: Add metadata based on source
                var streamOptions = new AzureBlobEgressStreamOptions();
                streamOptions.ContentType = contentType;

                var endpoint = new AzureBlobEgressEndpoint(_endpointOptions);
                string blobUri = await endpoint.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("uri", blobUri);
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

                var endpoint = new AzureBlobEgressEndpoint(_endpointOptions);
                string blobUri = await endpoint.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("uri", blobUri);
            }
        }
    }
}
