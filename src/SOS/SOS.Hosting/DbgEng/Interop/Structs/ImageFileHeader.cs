// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public struct IMAGE_FILE_HEADER
    {
        [FieldOffset(0)]
        public ushort Machine;
        [FieldOffset(2)]
        public ushort NumberOfSections;
        [FieldOffset(4)]
        public uint TimeDateStamp;
        [FieldOffset(8)]
        public uint PointerToSymbolTable;
        [FieldOffset(12)]
        public uint NumberOfSymbols;
        [FieldOffset(16)]
        public ushort SizeOfOptionalHeader;
        [FieldOffset(18)]
        public ushort Characteristics;
    }
}
