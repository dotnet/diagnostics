// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ElfSectionHeader64
    {
        public uint NameIndex;               // sh_name
        public ElfSectionHeaderType Type;   // sh_type
        public ulong Flags;                 // sh_flags
        public ulong VirtualAddress;        // sh_addr
        public ulong FileOffset;            // sh_offset
        public ulong FileSize;              // sh_size
        public uint Link;                   // sh_link
        public uint Info;                   // sh_info
        public ulong Alignment;             // sh_addralign
        public ulong EntrySize;             // sh_entsize
    }
}
