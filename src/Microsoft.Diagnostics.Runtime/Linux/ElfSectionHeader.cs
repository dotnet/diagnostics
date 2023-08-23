// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed class ElfSectionHeader
    {
        public ElfSectionHeaderType Type { get; }

        public uint NameIndex { get; }

        public ulong VirtualAddress { get; }

        public ulong FileOffset { get; }

        public ulong FileSize { get; }

        public ElfSectionHeader(Reader reader, bool is64bit, ulong headerPositon)
        {
            if (is64bit)
            {
                ElfSectionHeader64 header = reader.Read<ElfSectionHeader64>(headerPositon);
                Type = header.Type;
                NameIndex = header.NameIndex;
                VirtualAddress = header.VirtualAddress;
                FileOffset = header.FileOffset;
                FileSize = header.FileSize;
            }
            else
            {
                ElfSectionHeader32 header = reader.Read<ElfSectionHeader32>(headerPositon);
                Type = header.Type;
                NameIndex = header.NameIndex;
                VirtualAddress = header.VirtualAddress;
                FileOffset = header.FileOffset;
                FileSize = header.FileSize;
            }
        }
    }
}
