// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    internal class AzureBlobEgressEndpoint : EgressEndpoint<AzureBlobEgressStreamOptions>
    {
        private readonly AzureBlobEgressEndpointOptions _settings;

        public AzureBlobEgressEndpoint(AzureBlobEgressEndpointOptions settings)
        {
            _settings = settings;
        }

        public override async Task<string> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string name,
            AzureBlobEgressStreamOptions options,
            CancellationToken token)
        {
            var serviceUriBuilder = new UriBuilder(_settings.AccountUri)
            {
                Query = _settings.SharedAccessSignature
            };

            BlobServiceClient serviceClient = new BlobServiceClient(serviceUriBuilder.Uri);

            BlobContainerClient containerClient = serviceClient.GetBlobContainerClient(_settings.ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: token);

            string blobName = name;
            if (!string.IsNullOrEmpty(_settings.BlobDirectory))
            {
                blobName = string.Concat(_settings.BlobDirectory, "/", name);
            }

            BlockBlobClient blobClient = containerClient.GetBlockBlobClient(blobName);

            // Write blob content
            using (Stream blobStream = await blobClient.OpenWriteAsync(overwrite: true, cancellationToken: token))
            {
                await action(blobStream, token);

                await blobStream.FlushAsync(token);
            }

            // Write blob headers
            BlobHttpHeaders headers = new BlobHttpHeaders();
            headers.ContentType = options.ContentType;
            await blobClient.SetHttpHeadersAsync(headers, cancellationToken: token);

            // Write blob metadata
            await blobClient.SetMetadataAsync(options.Metadata, cancellationToken: token);

            // The BlobClient URI has the SAS token as the query parameter
            // Remove the SAS token before returning the URI
            UriBuilder outputBuilder = new UriBuilder(blobClient.Uri);
            outputBuilder.Query = null;

            return outputBuilder.Uri.AbsoluteUri;
        }
    }
}
