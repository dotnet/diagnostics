// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// A helper class to represent ELF program headers.
    /// </summary>
    internal sealed class ElfProgramHeader
    {
        private readonly ElfProgramHeaderAttributes _attributes;

        internal IAddressSpace AddressSpace { get; }

        /// <summary>
        /// The type of header this is.
        /// </summary>
        public ElfProgramHeaderType Type { get; }

        /// <summary>
        /// The VirtualAddress of this header.
        /// </summary>
        public ulong VirtualAddress { get; }

        /// <summary>
        /// The size of this header.
        /// </summary>
        public ulong VirtualSize { get; }

        /// <summary>
        /// The offset of this header within the file.
        /// </summary>
        public ulong FileOffset { get; }

        /// <summary>
        /// The size of this header within the file.
        /// </summary>
        public ulong FileSize { get; }

        /// <summary>
        /// Whether this section of memory is executable.
        /// </summary>
        public bool IsExecutable => (_attributes & ElfProgramHeaderAttributes.Executable) != 0;

        /// <summary>
        /// Whether this section of memory is writable.
        /// </summary>
        public bool IsWritable => (_attributes & ElfProgramHeaderAttributes.Writable) != 0;

        internal ElfProgramHeader(Reader reader, bool is64bit, ulong headerPositon, long loadBias, bool isVirtual = false)
        {
            if (is64bit)
            {
                ElfProgramHeader64 header = reader.Read<ElfProgramHeader64>(headerPositon);
                _attributes = (ElfProgramHeaderAttributes)header.Flags;
                Type = header.Type;
                VirtualAddress = header.VirtualAddress;
                VirtualSize = header.VirtualSize;
                FileOffset = header.FileOffset;
                FileSize = header.FileSize;
            }
            else
            {
                ElfProgramHeader32 header = reader.Read<ElfProgramHeader32>(headerPositon);
                _attributes = (ElfProgramHeaderAttributes)header.Flags;
                Type = header.Type;
                VirtualAddress = header.VirtualAddress;
                VirtualSize = header.VirtualSize;
                FileOffset = header.FileOffset;
                FileSize = header.FileSize;
            }

            if (isVirtual)
            {
                ulong offset = (loadBias < 0) ? (ulong)((long)VirtualAddress + loadBias) : VirtualAddress + (ulong)loadBias;
                AddressSpace = new RelativeAddressSpace(reader.DataSource, "ProgramHeader", offset, VirtualSize);
            }
            else
            {
                ulong offset = (loadBias < 0) ? (ulong)((long)FileOffset + loadBias) : FileOffset + (ulong)loadBias;
                AddressSpace = new RelativeAddressSpace(reader.DataSource, "ProgramHeader", offset, FileSize);
            }
        }
    }
}