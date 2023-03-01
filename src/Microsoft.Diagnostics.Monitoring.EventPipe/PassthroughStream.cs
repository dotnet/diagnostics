// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    /// <summary>
    /// A read-only stream that passes data as it is read to another stream.
    /// </summary>
    internal sealed class PassthroughStream : Stream
    {
        private readonly Stream _sourceStream;
        private readonly Stream _destinationStream;

        /// <summary>
        /// A read-only stream that passes data as it is read to another stream.
        /// </summary>
        /// <param name="sourceStream">The source stream that data will be read from.</param>
        /// <param name="destinationStream">The destination stream to pass read data to. It must either be full duplex or be write-only.</param>
        /// <param name="bufferSize">The size of the buffer to use when writing to the <paramref name="destinationStream"/>.</param>
        /// <param name="leaveDestinationStreamOpen">If true, the provided <paramref name="destinationStream"/> will not be automatically closed when this object is disposed.</param>
        public PassthroughStream(
            Stream sourceStream,
            Stream destinationStream,
            int bufferSize,
            bool leaveDestinationStreamOpen = false) : base()
        {
            _sourceStream = sourceStream;

            // Wrap a buffered stream around the destination stream to avoid
            // slowing down the data passthrough unless there is significant pressure.
            _destinationStream = new BufferedStream(
                leaveDestinationStreamOpen
                    ? new StreamLeaveOpenWrapper(destinationStream)
                    : destinationStream,
                bufferSize);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            int bytesRead = _sourceStream.Read(buffer);
            if (bytesRead != 0)
            {
                _destinationStream.Write(buffer[..bytesRead]);
            }

            return bytesRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int bytesRead = await _sourceStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead != 0)
            {
                await _destinationStream.WriteAsync(buffer[..bytesRead], cancellationToken);
            }

            return bytesRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override bool CanTimeout => _sourceStream.CanTimeout;
        public override long Length => _sourceStream.Length;

        public override long Position { get => _sourceStream.Position; set => throw new NotSupportedException(); }
        public override int ReadTimeout { get => _sourceStream.ReadTimeout; set => _sourceStream.ReadTimeout = value; }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void CopyTo(Stream destination, int bufferSize) => throw new NotSupportedException();
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public override void Flush() => _destinationStream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _destinationStream.FlushAsync(cancellationToken);

        public override async ValueTask DisposeAsync()
        {
            await _sourceStream.DisposeAsync();
            await _destinationStream.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
