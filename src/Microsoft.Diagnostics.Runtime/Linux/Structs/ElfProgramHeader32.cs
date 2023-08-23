// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ElfProgramHeader32
    {
        public ElfProgramHeaderType Type;   // p_type
        public uint FileOffset;             // p_offset
        public uint VirtualAddress;         // p_vaddr
        public uint PhysicalAddress;        // p_paddr
        public uint FileSize;               // p_filesz
        public uint VirtualSize;            // p_memsz
        public uint Flags;                  // p_flags
        public uint Alignment;              // p_align
    }
}