// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;

namespace ReleaseTool.Core
{
    public class AzureBlobBublisher : IPublisher
    {
        private const int MaxRetries = 15;
        private const int MaxFullLoopRetries = 5;
        private readonly TimeSpan FullLoopRetryDelay = TimeSpan.FromSeconds(1);

        private readonly string _accountName;
        private readonly string _clientId;
        private readonly string _containerName;
        private readonly string _releaseName;
        private readonly ILogger _logger;

        private BlobContainerClient _client;

        private Uri AccountBlobUri
        {
            get
            {
                return new Uri(FormattableString.Invariant($"https://{_accountName}.blob.core.windows.net"));
            }
        }

        private TokenCredential Credentials
        {
            get
            {
                if (_clientId == null)
                {
                    // Local development scenario. Use the default credential.
                    return new DefaultAzureCredential();
                }

                return new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = _clientId });
            }
        }

        private static BlobClientOptions BlobOptions
        {
            get
            {
                // The Azure SDK client has it's own built in retry logic
                // We want to allow more and longer retries because this
                // is a publishing operation that happens once and can be
                // allowed to take a very long time. We have a high
                // tolerance for slow operations and a low tolerance for failure.
                return new BlobClientOptions()
                {
                    Retry =
                    {
                        MaxRetries = MaxRetries,
                    }
                };
            }
        }

        public AzureBlobBublisher(string accountName, string clientId, string containerName, string releaseName, ILogger logger)
        {
            _accountName = accountName;
            _clientId = clientId;
            _containerName = containerName;
            _releaseName = releaseName;
            _logger = logger;
        }

        public void Dispose()
        {
        }

        public async Task<string> PublishFileAsync(FileMapping fileMap, CancellationToken ct)
        {
            Uri result = null;
            int retriesLeft = MaxFullLoopRetries;
            TimeSpan loopDelay = FullLoopRetryDelay;
            bool completed = false;

            do
            {
                _logger.LogInformation($"Attempting to publish {fileMap.RelativeOutputPath}, {retriesLeft} tries left.");
                try
                {
                    BlobContainerClient client = await GetClient(ct);
                    if (client == null)
                    {
                        // client creation failed, return
                        return null;
                    }

                    using FileStream srcStream = new(fileMap.LocalSourcePath, FileMode.Open, FileAccess.Read);

                    BlobClient blobClient = client.GetBlobClient(GetBlobName(_releaseName, fileMap.RelativeOutputPath));

                    await blobClient.UploadAsync(srcStream, overwrite: true, ct);

                    using BlobDownloadStreamingResult blobStream = (await blobClient.DownloadStreamingAsync(cancellationToken: ct)).Value;
                    srcStream.Position = 0;
                    completed = await VerifyFileStreamsMatchAsync(srcStream, blobStream, ct);

                    result = blobClient.Uri;
                }
                catch (IOException ioEx) when (ioEx is not PathTooLongException)
                {
                    _logger.LogWarning(ioEx, $"Failed to publish {fileMap.LocalSourcePath}, retries remaining: {retriesLeft}.");

                    /* Retry IO exceptions */
                    retriesLeft--;
                    loopDelay *= 2;

                    if (retriesLeft > 0)
                    {
                        await Task.Delay(loopDelay, ct);
                    }
                }
                catch (Exception ex)
                {
                    // Azure errors have their own built-in retry logic, so just abort if we got an AzureResponseException
                    _logger.LogWarning(ex, $"Failed to publish {fileMap.LocalSourcePath}, unexpected error, aborting.");
                    return null;
                }
            } while (retriesLeft > 0 && !completed);

            return result?.OriginalString;
        }

        private static string GetBlobName(string releaseName, string relativeFilePath)
        {
            return FormattableString.Invariant($"{releaseName}/{relativeFilePath}");
        }

        private async Task<BlobContainerClient> GetClient(CancellationToken ct)
        {
            if (_client == null)
            {
                BlobServiceClient serviceClient = new(AccountBlobUri, Credentials, BlobOptions);
                _logger.LogInformation($"Attempting to connect to {serviceClient.Uri} to store blobs.");

                BlobContainerClient newClient;
                int attemptCt = 0;
                do
                {
                    try
                    {
                        newClient = serviceClient.GetBlobContainerClient(_containerName);
                        if (!await newClient.ExistsAsync(ct))
                        {
                            newClient = await serviceClient.CreateBlobContainerAsync(_containerName, PublicAccessType.None, metadata: null, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to create or access {_containerName}, retrying with new name.");
                        continue;
                    }

                    _logger.LogInformation($"Container {_containerName} is ready.");
                    _client = newClient;
                    break;
                } while (++attemptCt < MaxFullLoopRetries);
            }

            if (_client == null)
            {
                _logger.LogError("Failed to create or access container for publishing drop.");
            }
            return _client;
        }

        private static async Task<bool> VerifyFileStreamsMatchAsync(FileStream srcStream, BlobDownloadStreamingResult destBlobDownloadStream, CancellationToken ct)
        {
            if (srcStream.Length != destBlobDownloadStream.Details.ContentLength)
            {
                return false;
            }

            using Stream destStream = destBlobDownloadStream.Content;

            using IMemoryOwner<byte> memOwnerSrc = MemoryPool<byte>.Shared.Rent(minBufferSize: 16_384);
            using IMemoryOwner<byte> memOwnerDest = MemoryPool<byte>.Shared.Rent(minBufferSize: 16_384);
            Memory<byte> memSrc = memOwnerSrc.Memory;
            Memory<byte> memDest = memOwnerDest.Memory;

            int bytesProcessed = 0;
            int srcBytesRemainingFromPrevRead = 0;
            int destBytesRemainingFromPrevRead = 0;

            while (bytesProcessed != srcStream.Length)
            {
                int srcBytesRead = await srcStream.ReadAsync(memSrc.Slice(srcBytesRemainingFromPrevRead), ct);
                srcBytesRead += srcBytesRemainingFromPrevRead;
                int destBytesRead = await destStream.ReadAsync(memDest.Slice(destBytesRemainingFromPrevRead), ct);
                destBytesRead += destBytesRemainingFromPrevRead;

                int bytesToCompare = Math.Min(srcBytesRead, destBytesRead);

                if (bytesToCompare == 0)
                {
                    return false;
                }

                bytesProcessed += bytesToCompare;
                srcBytesRemainingFromPrevRead = srcBytesRead - bytesToCompare;
                destBytesRemainingFromPrevRead = destBytesRead - bytesToCompare;

                bool isChunkEquals = memDest.Span.Slice(0, bytesToCompare).SequenceEqual(memSrc.Span.Slice(0, bytesToCompare));
                if (!isChunkEquals)
                {
                    return false;
                }

                memSrc.Slice(bytesToCompare, srcBytesRemainingFromPrevRead).CopyTo(memSrc);
                memDest.Slice(bytesToCompare, destBytesRemainingFromPrevRead).CopyTo(memDest);
            }

            return true;
        }
    }
}
