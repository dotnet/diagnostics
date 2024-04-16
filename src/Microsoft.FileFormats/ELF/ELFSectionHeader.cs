// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.FileFormats.ELF
{
    public enum ELFSectionHeaderType : uint
    {
        Null = 0,
        ProgBits = 1,
        SymTab = 2,
        StrTab = 3,
        Rela = 4,
        Hash = 5,
        Dynamic = 6,
        Note = 7,
        NoBits = 8,
        Rel = 9,
        ShLib = 10,
        DynSym = 11,
        InitArray = 14,
        FiniArray = 15,
        PreInitArray = 16,
        Group = 17,
        SymTabIndexes = 18,
        Num = 19,
        GnuAttributes = 0x6ffffff5,
        GnuHash = 0x6ffffff6,
        GnuLibList = 0x6ffffff7,
        CheckSum = 0x6ffffff8,
        GnuVerDef = 0x6ffffffd,
        GnuVerNeed = 0x6ffffffe,
        GnuVerSym = 0x6fffffff,
    }

    public class ELFSectionHeader : TStruct
    {
        public uint NameIndex;                  // sh_name
        public ELFSectionHeaderType Type;       // sh_type
        public SizeT Flags;                     // sh_flags
        public VirtualAddress VirtualAddress;   // sh_addr
        public FileOffset FileOffset;           // sh_offset
        public SizeT FileSize;                  // sh_size
        public uint Link;                       // sh_link
        public uint Info;                       // sh_info
        public SizeT Alignment;                 // sh_addralign
        public SizeT EntrySize;                 // sh_entsize
    }
}
