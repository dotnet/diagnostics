// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.FileFormats.ELF
{
    public enum ELFProgramHeaderType : uint
    {
        Null = 0,
        Load = 1,
        Dynamic = 2,
        Interp = 3,
        Note = 4,
        Shlib = 5,
        Phdr = 6,
        GnuEHFrame = 0x6474e550,
    }

    [Flags]
    public enum ELFProgramHeaderFlags : uint
    {
        Executable = 1,             // PF_X
        Writable = 2,               // PF_W
        Readable = 4,               // PF_R
        ReadWriteExecute = Executable | Writable | Readable,
        OSMask = 0x0FF00000,        // PF_MASKOS
        ProcessorMask = 0xF0000000, // PF_MASKPROC
    }

    public class ELFProgramHeader : TStruct
    {
        public ELFProgramHeaderType Type;       // p_type
        [If("64BIT")]
        public uint Flags;                      // p_flags
        public FileOffset FileOffset;           // p_offset
        public VirtualAddress VirtualAddress;   // p_vaddr
        public SizeT PhysicalAddress;           // p_paddr
        public SizeT FileSize;                  // p_filesz
        public SizeT VirtualSize;               // p_memsz
        [If("32BIT")]
        public uint Flags32;                    // p_flags
        public SizeT Alignment;                 // p_align
    }
}
