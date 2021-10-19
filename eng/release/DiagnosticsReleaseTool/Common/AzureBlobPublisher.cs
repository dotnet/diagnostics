using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public class AzureBlobBublisher : IPublisher
    {
        private const int ClockSkewSec = 15 * 60;
        private const int MaxRetries = 15;
        private const int MaxFullLoopRetries = 5;
        private readonly TimeSpan FullLoopRetryDelay = TimeSpan.FromSeconds(1);
        private const string AccessPolicyDownloadId = "DownloadDrop";

        private readonly string _accountName;
        private readonly string _accountKey;
        private readonly string _containerName;
        private readonly string _releaseName;
        private readonly int _sasValidDays;
        private readonly ILogger _logger;

        private BlobContainerClient _client;

        private Uri AccountBlobUri
        {
            get
            {
                return new Uri(FormattableString.Invariant($"https://{_accountName}.blob.core.windows.net"));
            }
        }

        private StorageSharedKeyCredential AccountCredential
        {
            get
            {
                StorageSharedKeyCredential credential = new StorageSharedKeyCredential(_accountName, _accountKey);
                return credential;
            }
        }

        private BlobClientOptions BlobOptions
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

        public AzureBlobBublisher(string accountName, string accountKey, string containerName, string releaseName, int sasValidDays, ILogger logger)
        {
            _accountName = accountName;
            _accountKey = accountKey;
            _containerName = containerName;
            _releaseName = releaseName;
            _sasValidDays = sasValidDays;
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

                    using var srcStream = new FileStream(fileMap.LocalSourcePath, FileMode.Open, FileAccess.Read);

                    BlobClient blobClient = client.GetBlobClient(GetBlobName(_releaseName, fileMap.RelativeOutputPath));

                    await blobClient.UploadAsync(srcStream, overwrite: true, ct);

                    BlobSasBuilder sasBuilder = new BlobSasBuilder()
                    {
                        BlobContainerName = client.Name,
                        BlobName = blobClient.Name,
                        Identifier = AccessPolicyDownloadId,
                        Protocol = SasProtocol.Https
                    };
                    Uri accessUri = blobClient.GenerateSasUri(sasBuilder);

                    using BlobDownloadStreamingResult blobStream = (await blobClient.DownloadStreamingAsync(cancellationToken: ct)).Value;
                    srcStream.Position = 0;
                    completed = await VerifyFileStreamsMatchAsync(srcStream, blobStream, ct);

                    result = accessUri;
                }
                catch (IOException ioEx) when (!(ioEx is PathTooLongException))
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
                BlobServiceClient serviceClient = new BlobServiceClient(AccountBlobUri, AccountCredential, BlobOptions);
                _logger.LogInformation($"Attempting to connect to {serviceClient.Uri} to store blobs.");

                BlobContainerClient newClient;
                int attemptCt = 0;
                do
                {
                    try
                    {
                        newClient = serviceClient.GetBlobContainerClient(_containerName);
                        if (!(await newClient.ExistsAsync(ct)).Value)
                        {
                            newClient = (await serviceClient.CreateBlobContainerAsync(_containerName, PublicAccessType.None, metadata: null, ct));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to create or access {_containerName}, retrying with new name.");
                        continue;
                    }

                    try
                    {
                        DateTime baseTime = DateTime.UtcNow;
                        // Add the new (or update existing) "download" policy to the container
                        // This is used to mint the SAS tokens without an expiration policy
                        // Expiration can be added later by modifying this policy
                        BlobSignedIdentifier downloadPolicyIdentifier = new BlobSignedIdentifier()
                        {
                            Id = AccessPolicyDownloadId,
                            AccessPolicy = new BlobAccessPolicy()
                            {
                                Permissions = "r",
                                PolicyStartsOn = new DateTimeOffset(baseTime.AddSeconds(-ClockSkewSec)),
                                PolicyExpiresOn = new DateTimeOffset(DateTime.UtcNow.AddDays(_sasValidDays).AddSeconds(ClockSkewSec)),
                            }
                        };
                        _logger.LogInformation($"Writing download access policy: {AccessPolicyDownloadId} to {_containerName}.");
                        await newClient.SetAccessPolicyAsync(PublicAccessType.None, new BlobSignedIdentifier[] { downloadPolicyIdentifier }, cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to write access policy for {_containerName}, retrying.");
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

        private async Task<bool> VerifyFileStreamsMatchAsync(FileStream srcStream, BlobDownloadStreamingResult destBlobDownloadStream, CancellationToken ct)
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