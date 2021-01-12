// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress.AzureStorage
{
    /// <summary>
    /// Egress provider for egressing stream data to an Azure blob storage account.
    /// </summary>
    /// <remarks>
    /// Blobs created through this provider will overwrite existing blobs if they have the same blob name.
    /// </remarks>
    internal class AzureBlobEgressProvider :
        EgressProvider<AzureBlobEgressProviderOptions, AzureBlobEgressStreamOptions>
    {
        public AzureBlobEgressProvider(AzureBlobEgressProviderOptions options, ILogger logger = null)
            : base(options, logger)
        {
        }

        public override async Task<string> EgressAsync(
            Func<CancellationToken, Task<Stream>> action,
            string name,
            AzureBlobEgressStreamOptions streamOptions,
            CancellationToken token)
        {
            LogAndValidateOptions(streamOptions, name);

            try
            {
                var containerClient = await GetBlobContainerClientAsync(token);

                BlobClient blobClient = containerClient.GetBlobClient(GetBlobName(name));

                Logger?.LogDebug("Start invoking stream action.");
                using var stream = await action(token);
                Logger?.LogDebug("End invoking stream action.");

                // Write blob content, headers, and metadata
                Logger?.LogDebug("Start uploading to storage with headers and metadata.");
                await blobClient.UploadAsync(stream, CreateHttpHeaders(streamOptions), streamOptions.Metadata, cancellationToken: token);
                Logger?.LogDebug("End uploading to storage with headers and metadata.");

                string blobUriString = GetBlobUri(blobClient);
                Logger?.LogDebug("Uploaded stream to {0}", blobUriString);
                return blobUriString;
            }
            catch (AggregateException ex) when (ex.InnerException is RequestFailedException innerException)
            {
                throw CreateException(innerException);
            }
            catch (RequestFailedException ex)
            {
                throw CreateException(ex);
            }
        }

        public override async Task<string> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string name,
            AzureBlobEgressStreamOptions streamOptions,
            CancellationToken token)
        {
            LogAndValidateOptions(streamOptions, name);

            try
            {
                var containerClient = await GetBlobContainerClientAsync(token);

                BlockBlobClient blobClient = containerClient.GetBlockBlobClient(GetBlobName(name));

                // Write blob content
                Logger?.LogDebug("Start uploading to storage.");
                using (Stream blobStream = await blobClient.OpenWriteAsync(overwrite: true, cancellationToken: token))
                {
                    await action(blobStream, token);

                    await blobStream.FlushAsync(token);
                }
                Logger?.LogDebug("End uploading to storage.");

                // Write blob headers
                Logger?.LogDebug("Start writing headers.");
                await blobClient.SetHttpHeadersAsync(CreateHttpHeaders(streamOptions), cancellationToken: token);
                Logger?.LogDebug("End writing headers.");

                // Write blob metadata
                Logger?.LogDebug("Start writing metadata.");
                await blobClient.SetMetadataAsync(streamOptions.Metadata, cancellationToken: token);
                Logger?.LogDebug("End writing metadata.");

                string blobUriString = GetBlobUri(blobClient);
                Logger?.LogDebug("Uploaded stream to {0}", blobUriString);
                return blobUriString;
            }
            catch (AggregateException ex) when (ex.InnerException is RequestFailedException innerException)
            {
                throw CreateException(innerException);
            }
            catch (RequestFailedException ex)
            {
                throw CreateException(ex);
            }
        }

        private void LogAndValidateOptions(AzureBlobEgressStreamOptions streamOptions, string fileName)
        {
            Logger?.LogProviderOption(nameof(Options.AccountKey), Options.AccountKey, redact: true);
            Logger?.LogProviderOption(nameof(Options.AccountUri), GetAccountUri(out _));
            Logger?.LogProviderOption(nameof(Options.BlobPrefix), Options.BlobPrefix);
            Logger?.LogProviderOption(nameof(Options.ContainerName), Options.ContainerName);
            Logger?.LogProviderOption(nameof(Options.SharedAccessSignature), Options.SharedAccessSignature, redact: true);
            Logger?.LogStreamOption(nameof(streamOptions.ContentEncoding), streamOptions.ContentEncoding);
            Logger?.LogStreamOption(nameof(streamOptions.ContentType), streamOptions.ContentType);
            Logger?.LogStreamOption(nameof(streamOptions.Metadata), "[" + string.Join(", ", streamOptions.Metadata.Keys) + "]");
            Logger?.LogDebug($"File name: {fileName}");

            ValidateOptions();
        }

        private Uri GetAccountUri(out string accountName)
        {
            var blobUriBuilder = new BlobUriBuilder(Options.AccountUri);
            blobUriBuilder.Query = null;
            blobUriBuilder.BlobName = null;
            blobUriBuilder.BlobContainerName = null;

            accountName = blobUriBuilder.AccountName;

            return blobUriBuilder.ToUri();
        }

        private async Task<BlobContainerClient> GetBlobContainerClientAsync(CancellationToken token)
        {
            BlobServiceClient serviceClient;
            if (!string.IsNullOrWhiteSpace(Options.SharedAccessSignature))
            {
                Logger?.LogDebug("Using shared access signature.");

                var serviceUriBuilder = new UriBuilder(Options.AccountUri)
                {
                    Query = Options.SharedAccessSignature
                };

                serviceClient = new BlobServiceClient(serviceUriBuilder.Uri);
            }
            else if (!string.IsNullOrEmpty(Options.AccountKey))
            {
                Logger?.LogDebug("Using account key.");

                // Remove Query in case SAS token was specified
                Uri accountUri = GetAccountUri(out string accountName);

                StorageSharedKeyCredential credential = new StorageSharedKeyCredential(
                    accountName,
                    Options.AccountKey);

                serviceClient = new BlobServiceClient(accountUri, credential);
            }
            else
            {
                throw CreateException("SharedAccessSignature or AccountKey must be specified.");
            }

            Logger?.LogDebug("Start creating blob container if not exists.");
            BlobContainerClient containerClient = serviceClient.GetBlobContainerClient(Options.ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: token);
            Logger?.LogDebug("End creating blob container if not exists.");

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
            headers.ContentEncoding = streamOptions.ContentEncoding;
            headers.ContentType = streamOptions.ContentType;
            return headers;
        }

        private static string GetBlobUri(BlobBaseClient client)
        {
            // The BlobClient URI has the SAS token as the query parameter
            // Remove the SAS token before returning the URI
            UriBuilder outputBuilder = new UriBuilder(client.Uri);
            outputBuilder.Query = null;

            return outputBuilder.Uri.ToString();
        }

        private static EgressException CreateException(string message)
        {
            return new EgressException(WrapMessage(message));
        }

        private static EgressException CreateException(Exception innerException)
        {
            return new EgressException(WrapMessage(innerException.Message), innerException);
        }

        private static string WrapMessage(string innerMessage)
        {
            if (!string.IsNullOrEmpty(innerMessage))
            {
                return $"Azure blob egress failed: {innerMessage}";
            }
            else
            {
                return "Azure blob egress failed.";
            }
        }
    }
}
