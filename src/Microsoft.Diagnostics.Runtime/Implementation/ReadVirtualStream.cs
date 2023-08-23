// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ReadVirtualStream : Stream
    {
        private long _pos;
        private readonly long _disp;
        private long _len;
        private readonly IDataReader _dataReader;

        public ReadVirtualStream(IDataReader dataReader, long displacement, long len)
        {
            _dataReader = dataReader;
            _disp = displacement;
            _len = len;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        public override void Flush()
        {
        }

        public override long Length => _len;

        public override long Position
        {
            get => _pos;
            set
            {
                _pos = value;
                if (_pos > _len)
                    _pos = _len;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _dataReader.Read((ulong)(_pos + _disp), new Span<byte>(buffer, offset, count));
            if (read > 0)
            {
                _pos += read;
                return read;
            }

            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    _pos = offset;
                    break;

                case SeekOrigin.End:
                    _pos = _len + offset;
                    if (_pos > _len)
                        _pos = _len;
                    break;

                case SeekOrigin.Current:
                    _pos += offset;
                    if (_pos > _len)
                        _pos = _len;
                    break;
            }

            return _pos;
        }

        public override void SetLength(long value)
        {
            _len = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException($"Cannot write to a {nameof(ReadVirtualStream)}.");
        }
    }
}