// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats.Minidump
{
    public class MinidumpVirtualAddressSpace : IAddressSpace
    {
        private readonly IAddressSpace _addressSpace;
        private readonly ReadOnlyCollection<MinidumpSegment> _segments;
        private readonly ulong _length;

        public ulong Length
        {
            get
            {
                return _length;
            }
        }

        public MinidumpVirtualAddressSpace(ReadOnlyCollection<MinidumpSegment> segments, IAddressSpace addressSpace)
        {
            _addressSpace = addressSpace;
            _segments = segments;
            MinidumpSegment last = segments.Last();
            _length = last.VirtualAddress + last.Size;
        }

        public uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            if (count == 0)
            {
                return 0;
            }
            MinidumpSegment seg = FindSegment(position);
            if (seg == null)
            {
                return 0;
            }
            // TODO: What if they read past the end of the segment?
            Debug.Assert(position >= seg.VirtualAddress);
            ulong offset = position - seg.VirtualAddress + seg.FileOffset;
            return _addressSpace.Read(offset, buffer, bufferOffset, count);
        }

        private MinidumpSegment FindSegment(ulong position)
        {
            int min = 0;
            int max = _segments.Count - 1;

            while (min <= max)
            {
                int mid = (min + max) / 2;
                MinidumpSegment current = _segments[mid];

                if (position < current.VirtualAddress)
                {
                    max = mid - 1;
                }
                else if (position >= current.VirtualAddress + current.Size)
                {
                    min = mid + 1;
                }
                else
                {
                    Debug.Assert(current.Contains(position));
                    return current;
                }
            }

            return null;
        }
    }
}
