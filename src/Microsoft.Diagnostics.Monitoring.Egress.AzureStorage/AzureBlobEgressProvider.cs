// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    internal class AzureBlobEgressProvider : EgressProvider<AzureBlobEgressProviderOptions, AzureBlobEgressStreamOptions>
    {
        public AzureBlobEgressProvider(AzureBlobEgressProviderOptions options)
            : base(options)
        {
        }

        public override async Task<string> EgressAsync(
            Func<CancellationToken, Task<Stream>> action,
            string name,
            AzureBlobEgressStreamOptions streamOptions,
            CancellationToken token)
        {
            var containerClient = await GetBlobContainerClientAsync(token);

            BlobClient blobClient = containerClient.GetBlobClient(GetBlobName(name));

            using var stream = await action(token);

            // Write blob content, headers, and metadata
            await blobClient.UploadAsync(stream, CreateHttpHeaders(streamOptions), streamOptions.Metadata, cancellationToken: token);

            return GetBlobUri(blobClient);
        }

        public override async Task<string> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string name,
            AzureBlobEgressStreamOptions streamOptions,
            CancellationToken token)
        {
            var containerClient = await GetBlobContainerClientAsync(token);

            BlockBlobClient blobClient = containerClient.GetBlockBlobClient(GetBlobName(name));

            // Write blob content
            using (Stream blobStream = await blobClient.OpenWriteAsync(overwrite: true, cancellationToken: token))
            {
                await action(blobStream, token);

                await blobStream.FlushAsync(token);
            }

            // Write blob headers
            await blobClient.SetHttpHeadersAsync(CreateHttpHeaders(streamOptions), cancellationToken: token);

            // Write blob metadata
            await blobClient.SetMetadataAsync(streamOptions.Metadata, cancellationToken: token);

            return GetBlobUri(blobClient);
        }

        private async Task<BlobContainerClient> GetBlobContainerClientAsync(CancellationToken token)
        {
            BlobServiceClient serviceClient;
            if (!string.IsNullOrWhiteSpace(Options.SharedAccessSignature))
            {
                var serviceUriBuilder = new UriBuilder(Options.AccountUri)
                {
                    Query = Options.SharedAccessSignature
                };

                serviceClient = new BlobServiceClient(serviceUriBuilder.Uri);
            }
            else if (!string.IsNullOrEmpty(Options.AccountKey))
            {
                var serviceUri = new Uri(Options.AccountUri);

                var blobUriBuilder = new BlobUriBuilder(serviceUri);

                StorageSharedKeyCredential credential = new StorageSharedKeyCredential(
                    blobUriBuilder.AccountName,
                    Options.AccountKey);

                serviceClient = new BlobServiceClient(serviceUri, credential);
            }
            else
            {
                throw new InvalidOperationException("SharedAccessSignature or AccountKey must be specified.");
            }

            BlobContainerClient containerClient = serviceClient.GetBlobContainerClient(Options.ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: token);

            return containerClient;
        }

        private string GetBlobName(string fileName)
        {
            if (string.IsNullOrEmpty(Options.BlobPrefix))
            {
                return fileName;
            }
            else
            {
                return string.Concat(Options.BlobPrefix, "/", fileName);
            }
        }

        private BlobHttpHeaders CreateHttpHeaders(AzureBlobEgressStreamOptions streamOptions)
        {
            BlobHttpHeaders headers = new BlobHttpHeaders();
            headers.ContentType = streamOptions.ContentType;
            return headers;
        }

        private static string GetBlobUri(BlobBaseClient client)
        {
            // The BlobClient URI has the SAS token as the query parameter
            // Remove the SAS token before returning the URI
            UriBuilder outputBuilder = new UriBuilder(client.Uri);
            outputBuilder.Query = null;

            return outputBuilder.Uri.AbsoluteUri;
        }
    }
}
