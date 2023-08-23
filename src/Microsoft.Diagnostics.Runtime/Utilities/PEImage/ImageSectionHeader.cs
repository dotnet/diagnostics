// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ImageSectionHeader
    {
        public string Name
        {
            get
            {
                fixed (byte* ptr = NameBytes)
                {
                    if (ptr[7] == 0)
                        return Marshal.PtrToStringAnsi((IntPtr)ptr)!;

                    return Marshal.PtrToStringAnsi((IntPtr)ptr, 8);
                }
            }
        }

        public fixed byte NameBytes[8];
        public uint VirtualSize;
        public uint VirtualAddress;
        public uint SizeOfRawData;
        public uint PointerToRawData;
        public uint PointerToRelocations;
        public uint PointerToLinenumbers;
        public ushort NumberOfRelocations;
        public ushort NumberOfLinenumbers;
        public uint Characteristics;
    }
}
