// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.Egress;
using Microsoft.Diagnostics.Monitoring.Egress.AzureStorage;
using Microsoft.Diagnostics.Monitoring.Egress.FileSystem;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal class EgressService : IEgressService
    {
        private readonly IOptionsMonitor<EgressOptions> _egressOptions;
        private readonly IOptionsMonitor<AzureStorageOptions> _azureStorageOptions;

        public EgressService(
            IOptionsMonitor<EgressOptions> egressOptions,
            IOptionsMonitor<AzureStorageOptions> azureStorageOptions)
        {
            _egressOptions = egressOptions;
            _azureStorageOptions = azureStorageOptions;
        }

        public Task<EgressResult> EgressAsync(string endpointName, Func<CancellationToken, Task<Stream>> action, string fileName, string contentType, IEndpointInfo source, CancellationToken token)
        {
            if (_egressOptions.CurrentValue.Endpoints.TryGetValue(endpointName, out EgressEndpointOptions template))
            {
                switch (template)
                {
                    case AzureBlobEgressEndpointOptions azureBlobSettings:
                        return EgressAsync(
                            azureBlobSettings,
                            (endpoint, options, token) => endpoint.EgressAsync(action, fileName, options, token),
                            endpointName,
                            contentType,
                            source,
                            token);
                    case FileSystemEgressEndpointOptions fileSystemSettings:
                        return EgressAsync(
                            fileSystemSettings,
                            (endpoint, options, token) => endpoint.EgressAsync(action, fileName, options, token),
                            token);
                    default:
                        throw new NotSupportedException(FormattableString.Invariant($"Egress settings '{template.GetType().Name}' is not supported."));
                }
            }
            throw new InvalidOperationException(FormattableString.Invariant($"Egress endpoint '{endpointName}' does not exist."));
        }

        public Task<EgressResult> EgressAsync(string endpointName, Func<Stream, CancellationToken, Task> action, string fileName, string contentType, IEndpointInfo source, CancellationToken token)
        {
            if (_egressOptions.CurrentValue.Endpoints.TryGetValue(endpointName, out EgressEndpointOptions template))
            {
                switch (template)
                {
                    case AzureBlobEgressEndpointOptions azureBlobSettings:
                        return EgressAsync(
                            azureBlobSettings,
                            (endpoint, options, token) => endpoint.EgressAsync(action, fileName, options, token),
                            endpointName,
                            contentType,
                            source,
                            token);
                    case FileSystemEgressEndpointOptions fileSystemSettings:
                        return EgressAsync(
                            fileSystemSettings,
                            (endpoint, options, token) => endpoint.EgressAsync(action, fileName, options, token),
                            token);
                    default:
                        throw new NotSupportedException(FormattableString.Invariant($"Egress settings '{template.GetType().Name}' is not supported."));
                }
            }
            throw new InvalidOperationException(FormattableString.Invariant($"Egress endpoint '{endpointName}' does not exist."));
        }

        private async Task<EgressResult> EgressAsync(AzureBlobEgressEndpointOptions template, Func<AzureBlobEgressEndpoint, AzureBlobEgressStreamOptions, CancellationToken, Task<EgressResult>> action, string endpointName, string contentType, IEndpointInfo source, CancellationToken token)
        {
            var endpointOptions = new AzureBlobEgressEndpointOptions(template);

            // If SAS token is not specified, use the one from the SAS token configuration that has
            // a name that matches the endpoint name.
            if (string.IsNullOrEmpty(endpointOptions.SasToken) &&
                _azureStorageOptions.CurrentValue.SasTokens.TryGetValue(endpointName, out string sasToken))
            {
                endpointOptions.SasToken = sasToken;
            }

            var endpoint = new AzureBlobEgressEndpoint(endpointOptions);

            // TODO: Add metadata
            var streamOptions = new AzureBlobEgressStreamOptions();
            streamOptions.ContentType = contentType;

            return await action(endpoint, streamOptions, token);
        }

        private Task<EgressResult> EgressAsync(FileSystemEgressEndpointOptions template, Func<FileSystemEgressEndpoint, FileSystemEgressStreamOptions, CancellationToken, Task<EgressResult>> action, CancellationToken token)
        {
            var endpointOptions = new FileSystemEgressEndpointOptions(template);

            var endpoint = new FileSystemEgressEndpoint(endpointOptions);

            var streamOptions = new FileSystemEgressStreamOptions();

            return action(endpoint, streamOptions, token);
        }
    }
}
