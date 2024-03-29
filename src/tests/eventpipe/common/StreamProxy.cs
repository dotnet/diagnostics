// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.IO;

namespace EventPipe.UnitTests.Common
{
    // This Stream implementation takes one stream
    // and proxies the Stream API to it while
    // saving any read bytes to an internal stream.
    // Should an error occur, the internal stream
    // is dumped to disk for reproducing the error.
    public class StreamProxy : Stream
    {
        private Stream ProxiedStream { get; }
        private static MemoryStream InternalStream => new();
        public override bool CanRead => ProxiedStream.CanRead;

        public override bool CanSeek => ProxiedStream.CanSeek;

        public override bool CanWrite => ProxiedStream.CanWrite;

        public override long Length => ProxiedStream.Length;

        public override long Position { get => ProxiedStream.Position; set => ProxiedStream.Position = value; }

        public StreamProxy(Stream streamToProxy)
        {
            ProxiedStream = streamToProxy;
        }

        public override void Flush() => ProxiedStream.Flush();

        // Read the actual desired amount of bytes into an empty buffer
        // copy those bytes into an internal MemoryStream THEN
        // forward those bytes to the caller. If the caller
        // would have thrown an exception with its Read, copy
        // the bytes to the internal MemoryStream and then throw
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null || offset < 0 || count < 0)
            {
                throw new ArgumentException("Invalid input into Read");
            }

            byte[] localBuffer = ArrayPool<byte>.Shared.Rent(count);
            int readCount = ProxiedStream.Read(localBuffer, 0, count);
            if (readCount == 0)
            {
                return readCount;
            }

            StreamProxy.InternalStream.Write(localBuffer, 0, readCount);

            if (buffer.Length - offset < count)
            {
                // This is the error that EventPipeEventSource is causing,
                // so this is when we should throw an exception, just like
                // System.IO.PipeStream. This will result in the dispose method
                // being called and the culprit stream data being dumped to disk
                Logger.logger.Log($"[Error] Attempted to read {count} bytes into a buffer of length {buffer.Length} at offset {offset}");

                // Throw the exception like what would have happened in System.IO.PipeStream
                throw new ArgumentException($"Attempted to read {count} bytes into a buffer of length {buffer.Length} at offset {offset}");
            }

            // copy the data into the caller's buffer
            Array.Copy(localBuffer, 0, buffer, offset, readCount);

            ArrayPool<byte>.Shared.Return(localBuffer, true);
            return readCount;
        }

        public override long Seek(long offset, SeekOrigin origin) => ProxiedStream.Seek(offset, origin);

        public override void SetLength(long value) => ProxiedStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            // This stream is only for "reading" from. No need for this method.
            throw new NotImplementedException();
        }

        protected bool disposed;
        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    ProxiedStream.Dispose();
                    StreamProxy.InternalStream.Dispose();
                }

                disposed = true;
            }
        }

        public void DumpStreamToDisk()
        {
            string streamDumpDir = System.Environment.GetEnvironmentVariable("_PIPELINE_STREAMDUMPDIR") ?? Path.GetTempPath();
            Logger.logger.Log($"\t streamDumpDir = {streamDumpDir}");
            string filePath = Path.Combine(streamDumpDir, Path.GetRandomFileName() + ".nettrace");
            using (FileStream streamDumpFile = File.Create(filePath))
            {
                Logger.logger.Log($"\t Writing stream for PID {System.Diagnostics.Process.GetCurrentProcess().Id} to {filePath}");
                StreamProxy.InternalStream.Seek(0, SeekOrigin.Begin);
                StreamProxy.InternalStream.CopyTo(streamDumpFile);
            }
        }
    }
}
