// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// An address space that starts at a fixed offset relative to another space
    /// </summary>
    public class RelativeAddressSpace : IAddressSpace
    {
        private IAddressSpace _baseAddressSpace;
        private ulong _baseStart;
        private ulong _length;
        private long _baseToRelativeShift;


        public RelativeAddressSpace(IAddressSpace baseAddressSpace, ulong startOffset, ulong length) :
            this(baseAddressSpace, startOffset, length, -(long)startOffset)
        { }

        public RelativeAddressSpace(IAddressSpace baseAddressSpace, ulong startOffset, ulong length, long baseToRelativeShift)
        {
            /*
            if (startOffset < 0 || startOffset >= baseAddressSpace.Length)
            {
                throw new BadInputFormatException("Invalid startOffset");
            }
            if (length < 0 || startOffset + length > baseAddressSpace.Length)
            {
                throw new BadInputFormatException("Invalid length");
            }
            if((long)startOffset + baseToRelativeShift < 0)
            {
                throw new BadInputFormatException("Invalid baseToRelativeShift");
            }*/
            _baseAddressSpace = baseAddressSpace;
            _baseStart = startOffset;
            _length = length;
            _baseToRelativeShift = baseToRelativeShift;
        }

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
            ulong basePosition = (ulong)((long)position - _baseToRelativeShift);
            if (basePosition < _baseStart)
            {
                return 0;
            }
            count = (uint)Math.Min(count, _length);
            return _baseAddressSpace.Read(basePosition, buffer, bufferOffset, count);
        }

        /// <summary>
        /// The upper bound (non-inclusive) of readable addresses
        /// </summary>
        public ulong Length { get { return unchecked(_baseStart + _length + (ulong)_baseToRelativeShift); } }
    }

    public class ZeroAddressSpace : IAddressSpace
    {
        public ZeroAddressSpace(ulong length)
        {
            Length = length;
        }

        public ulong Length { get; private set; }

        public uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            if (position >= Length)
            {
                return 0;
            }
            count = (uint)Math.Min(Length - position, count);
            Array.Clear(buffer, (int)bufferOffset, (int)count);
            return count;
        }
    }

    public struct PiecewiseAddressSpaceRange
    {
        public ulong Start;
        public ulong Length;
        public IAddressSpace AddressSpace;
    }

    public class PiecewiseAddressSpace : IAddressSpace
    {
        private PiecewiseAddressSpaceRange[] _ranges;

        public PiecewiseAddressSpace(params PiecewiseAddressSpaceRange[] ranges)
        {
            _ranges = ranges;
            Length = _ranges.Max(r => r.Start + r.Length);
        }

        public ulong Length { get; private set; }

        public uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            uint bytesRead = 0;
            while (bytesRead != count)
            {
                int i = 0;
                for (; i < _ranges.Length; i++)
                {
                    ulong upper = _ranges[i].Start + _ranges[i].Length;
                    if (_ranges[i].Start <= position && position < upper)
                    {
                        uint bytesToReadRange = (uint)Math.Min(count - bytesRead, upper - position);
                        uint bytesReadRange = _ranges[i].AddressSpace.Read(position, buffer, bufferOffset, bytesToReadRange);
                        if (bytesReadRange == 0)
                        {
                            return bytesRead;
                        }
                        position += bytesReadRange;
                        bufferOffset += bytesReadRange;
                        bytesRead += bytesReadRange;
                        break;
                    }
                }
                if (i == _ranges.Length)
                {
                    return bytesRead;
                }
            }
            return bytesRead;
        }
    }
}
