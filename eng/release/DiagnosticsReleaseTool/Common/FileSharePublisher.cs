// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public class FileSharePublisher : IPublisher
    {
        private const int MaxRetries = 5;
        private const int DelayMsec = 100;

        private readonly string _sharePath;

        public FileSharePublisher(string sharePath)
        {
            _sharePath = sharePath;
        }

        public void Dispose() { }

        public async Task<string> PublishFileAsync(FileMapping fileData, CancellationToken ct)
        {
            // TODO: Be resilient to "can't cancel case".
            string destinationUri = Path.Combine(_sharePath, fileData.RelativeOutputPath);
            FileInfo fi;

            try
            {
                fi = new FileInfo(destinationUri);
            }
            catch (Exception)
            {
                // TODO: We probably want logging here.
                return null;
            }

            int retries = 0;
            int delay = 0;
            bool completed = false;

            try
            {
                if (fi.Exists && fi.Attributes.HasFlag(FileAttributes.Directory))
                {
                    // Filestream will deal with files, but not directories
                    Directory.Delete(destinationUri, recursive: true);
                }
                fi.Directory.Create();
            }
            catch
            {
                // Pretty much exvery exception on this path is terminal.
                // We have mostly permissions or file share names wrong.
                return null;
            }

            do
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);

                try
                {
                    using var srcStream = new FileStream(fileData.LocalSourcePath, FileMode.Open, FileAccess.Read);
                    using var destStream = new FileStream(destinationUri, FileMode.Create, FileAccess.ReadWrite);
                    await srcStream.CopyToAsync(destStream, ct).ConfigureAwait(false);

                    destStream.Position = 0;
                    srcStream.Position = 0;

                    completed = await VerifyFileStreamsMatchAsync(srcStream, destStream, ct).ConfigureAwait(false);
                }
                catch (IOException ex) when (ex is not (PathTooLongException or FileNotFoundException or DirectoryNotFoundException))
                {
                    /* Retry IO exceptions */
                }
                catch (Exception)
                {
                    return null;
                }

                retries++;
                delay = delay * 2 + DelayMsec;
            } while (retries < MaxRetries && !completed);

            return destinationUri;
        }

        private async Task<bool> VerifyFileStreamsMatchAsync(FileStream srcStream, FileStream destStream, CancellationToken ct)
        {
            if (srcStream.Length != destStream.Length)
            {
                return false;
            }

            using IMemoryOwner<byte> memOwnerSrc = MemoryPool<byte>.Shared.Rent(minBufferSize: 16_384);
            using IMemoryOwner<byte> memOwnerDest = MemoryPool<byte>.Shared.Rent(minBufferSize: 16_384);
            Memory<byte> memSrc = memOwnerSrc.Memory;
            Memory<byte> memDest = memOwnerDest.Memory;

            int bytesProcessed = 0;
            int srcBytesRemainingFromPrevRead = 0;
            int destBytesRemainingFromPrevRead = 0;

            while (bytesProcessed != srcStream.Length)
            {
                int srcBytesRead = await srcStream.ReadAsync(memSrc.Slice(srcBytesRemainingFromPrevRead), ct).ConfigureAwait(false);
                srcBytesRead += srcBytesRemainingFromPrevRead;
                int destBytesRead = await destStream.ReadAsync(memDest.Slice(destBytesRemainingFromPrevRead), ct).ConfigureAwait(false);
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
