// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    /// <summary>
    /// Wraps a given stream but leaves it open on Dispose.
    /// </summary>
    internal sealed class StreamLeaveOpenWrapper : Stream
    {
        private readonly Stream _baseStream;

        public StreamLeaveOpenWrapper(Stream baseStream) : base()
        {
            _baseStream = baseStream;
        }

        public override bool CanSeek => _baseStream.CanSeek;

        public override bool CanTimeout => _baseStream.CanTimeout;

        public override bool CanRead => _baseStream.CanRead;

        public override bool CanWrite => _baseStream.CanWrite;

        public override long Length => _baseStream.Length;

        public override long Position { get => _baseStream.Position; set => _baseStream.Position = value; }

        public override int ReadTimeout { get => _baseStream.ReadTimeout; set => _baseStream.ReadTimeout = value; }

        public override int WriteTimeout { get => _baseStream.WriteTimeout; set => _baseStream.WriteTimeout = value; }

        public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);

        public override int Read(Span<byte> buffer) => _baseStream.Read(buffer);

        public override int Read(byte[] buffer, int offset, int count) => _baseStream.Read(buffer, offset, count);

        public override int ReadByte() => _baseStream.ReadByte();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _baseStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _baseStream.ReadAsync(buffer, cancellationToken);

        public override void Flush() => _baseStream.Flush();

        public override void SetLength(long value) => _baseStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _baseStream.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => _baseStream.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _baseStream.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _baseStream.WriteAsync(buffer, cancellationToken);

        public override void WriteByte(byte value) => _baseStream.WriteByte(value);

        public override Task FlushAsync(CancellationToken cancellationToken) => _baseStream.FlushAsync(cancellationToken);

        public override void CopyTo(Stream destination, int bufferSize) => _baseStream.CopyTo(destination, bufferSize);

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => _baseStream.CopyToAsync(destination, bufferSize, cancellationToken);

        public override async ValueTask DisposeAsync() => await base.DisposeAsync();
    }
}
