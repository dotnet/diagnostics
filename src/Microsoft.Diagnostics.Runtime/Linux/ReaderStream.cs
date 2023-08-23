// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed class ReaderStream : Stream
    {
        private readonly Reader _reader;
        private readonly ulong _baseAddress;
        private readonly ulong _length;
        private long _position;

        public ReaderStream(ulong baseAddress, ulong length, Reader reader)
        {
            _reader = reader;
            _length = length;
            _baseAddress = baseAddress;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => (long)_length;

        public override long Position { get => _position; set => _position = value; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (offset != 0)
                throw new NotImplementedException();

            ulong readOffset = (_position < 0) ? (ulong)((long)_baseAddress + _position) : _baseAddress + (ulong)_position;
            int read = _reader.ReadBytes(readOffset, new Span<byte>(buffer, 0, count));
            DebugOnly.Assert(read >= 0);
            _position += read;

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
                _position = offset;
            else if (origin == SeekOrigin.Current)
                _position += offset;
            else
                throw new InvalidOperationException();

            return _position;
        }

        public override void SetLength(long value)
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }
    }
}
