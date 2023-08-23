// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ElfSectionHeader32
    {
        public uint NameIndex;               // sh_name
        public ElfSectionHeaderType Type;   // sh_type
        public uint Flags;                  // sh_flags
        public uint VirtualAddress;         // sh_addr
        public uint FileOffset;             // sh_offset
        public uint FileSize;               // sh_size
        public uint Link;                   // sh_link
        public uint Info;                   // sh_info
        public uint Alignment;              // sh_addralign
        public uint EntrySize;              // sh_entsize
    }
}