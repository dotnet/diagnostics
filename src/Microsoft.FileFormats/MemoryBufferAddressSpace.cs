// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// Creates an address space that reads from a byte array in memory.
    /// </summary>
    public sealed class MemoryBufferAddressSpace : IAddressSpace
    {
        public MemoryBufferAddressSpace(IEnumerable<byte> bytes)
        {
            _bytes = bytes.ToArray();
            Length = (ulong)_bytes.Length;
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
            if (position >= Length || position + count > Length)
            {
                throw new BadInputFormatException("Unexpected end of data: Expected " + count + " bytes.");
            }
            Array.Copy(_bytes, (int)position, buffer, (int)bufferOffset, (int)count);
            return count;
        }

        private byte[] _bytes;
    }
}
