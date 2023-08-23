// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed class StreamAddressSpace : IAddressSpace
    {
        private readonly object _sync = new();
        private readonly Stream _stream;

        public StreamAddressSpace(Stream stream)
        {
            _stream = stream;
        }

        public ulong Length => (uint)_stream.Length;
        public string Name => _stream.GetFilename() ?? _stream.GetType().Name;

        public int Read(ulong position, Span<byte> buffer)
        {
            lock (_sync)
            {
                _stream.Seek((long)position, SeekOrigin.Begin);
                return _stream.Read(buffer);
            }
        }
    }
}
