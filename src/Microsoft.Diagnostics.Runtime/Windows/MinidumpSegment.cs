// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal readonly struct MinidumpSegment
    {
        public MinidumpSegment(ulong offset, ulong startAddress, ulong size)
        {
            FileOffset = offset;
            VirtualAddress = startAddress;
            Size = size;
        }

        public override string ToString() => $"{VirtualAddress:x}-{VirtualAddress + Size:x}";

        public bool Contains(ulong address) => VirtualAddress <= address && address < VirtualAddress + Size;

        public ulong FileOffset { get; }
        public ulong VirtualAddress { get; }
        public ulong Size { get; }

        public ulong End => VirtualAddress + Size;
    }
}