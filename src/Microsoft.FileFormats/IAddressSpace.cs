// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// Abstracts a flat randomly-accessible array of bytes
    /// </summary>
    public interface IAddressSpace
    {
        /// <summary>
        /// Reads a range of bytes from the address space
        /// </summary>
        /// <param name="position">The position in the address space to begin reading from</param>
        /// <param name="buffer">The buffer that will receive the bytes that are read</param>
        /// <param name="bufferOffset">The offset in the output buffer to begin writing the bytes</param>
        /// <param name="count">The number of bytes to read into the buffer</param>
        /// <returns>The number of bytes read</returns>
        uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count);

        /// <summary>
        /// The upper bound (non-inclusive) of readable addresses
        /// </summary>
        /// <remarks>
        /// Some address spaces may be sparse, there is no guarantee reads will succeed even
        /// at addresses less than the Length
        /// </remarks>
        ulong Length { get; }
    }

    public static class AddressSpaceExtensions
    {
        /// <summary>
        /// Read the specified number of bytes.
        /// </summary>
        /// <param name="addressSpace">The address space to read from</param>
        /// <param name="position">The position in the address space to start reading from</param>
        /// <param name="count">The number of bytes to read</param>
        /// <returns>
        /// Returns an array of exactly "count" bytes or throw an exception.
        /// </returns>
        /// <throws>
        /// BadInputFormatException to indicate an "unexpected end of stream" condition
        /// </throws>
        public static byte[] Read(this IAddressSpace addressSpace, ulong position, uint count)
        {
            byte[] bytes = ArrayHelper.New<byte>(count);
            if (count != addressSpace.Read(position, bytes, 0, count))
            {
                throw new BadInputFormatException("Unable to read bytes at offset 0x" + position.ToString("x"));
            }
            return bytes;
        }
    }
}
