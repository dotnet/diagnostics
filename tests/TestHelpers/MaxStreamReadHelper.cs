// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.FileFormats;

namespace TestHelpers
{
    public class MaxStreamReadHelper : IAddressSpace
    {
        private readonly IAddressSpace _addressSpace;

        public ulong Max { get; private set; }

        public MaxStreamReadHelper(IAddressSpace address)
        {
            _addressSpace = address;
        }

        public ulong Length
        {
            get
            {
                return _addressSpace.Length;
            }
        }

        public uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            ulong max = position + count;
            if (max > Max)
            {
                Max = max;
            }
            return _addressSpace.Read(position, buffer, bufferOffset, count);
        }
    }
}
