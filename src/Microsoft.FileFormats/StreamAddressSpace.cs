// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// Creates an address space that reads from a stream.
    /// </summary>
    public sealed class StreamAddressSpace : IAddressSpace, IDisposable
    {
        private Stream _stream;

        public StreamAddressSpace(Stream stream)
        {
            System.Diagnostics.Debug.Assert(stream.CanSeek);
            _stream = stream;
            Length = (ulong)stream.Length;
        }

        /// <summary>
        /// The upper bound (non-inclusive) of readable addresses
        /// </summary>
        public ulong Length { get; private set; }

        /// <summary>
        /// Reads a range of bytes from the address space
        /// </summary>
        /// <param name="position">The position in the address space to begin reading from</param>
        /// <param name="buffer">The buffer that will receive the bytes that are read</param>
        /// <param name="bufferOffset">The offset in the output buffer to begin writing the bytes</param>
        /// <param name="count">The number of bytes to read into the buffer</param>
        /// <returns>The number of bytes read</returns>
        public uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            if (_stream is null)
            {
                throw new ObjectDisposedException(nameof(_stream), "StreamAddressSpace instance has been disposed");
            }
            if (position + count > Length)
            {
                throw new BadInputFormatException("Unexpected end of data: Expected " + count + " bytes.");
            }
            _stream.Position = (long)position;
            return (uint)_stream.Read(buffer, (int)bufferOffset, (int)count);
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }
    }
}
