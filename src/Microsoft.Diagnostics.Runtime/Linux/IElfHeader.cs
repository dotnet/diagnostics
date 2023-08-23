// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// An abstract version of 32 and 64 bit ELF headers.
    /// </summary>
    internal interface IElfHeader
    {
        /// <summary>
        /// Whether this file is 64 bit or not.
        /// </summary>
        bool Is64Bit { get; }

        /// <summary>
        /// Whether this file contains the magic header at the right offset or not.
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// The type of ELF file.
        /// </summary>
        ElfHeaderType Type { get; }

        /// <summary>
        /// The architecture of the ELF file.
        /// </summary>
        ElfMachine Architecture { get; }

        /// <summary>
        /// The offset of the program header.
        /// </summary>
        ulong ProgramHeaderOffset { get; }

        /// <summary>
        /// The offset of the section header.
        /// </summary>
        ulong SectionHeaderOffset { get; }

        /// <summary>
        /// The size of program headers.
        /// </summary>
        ushort ProgramHeaderEntrySize { get; }

        /// <summary>
        /// The count of program headers.
        /// </summary>
        ushort ProgramHeaderCount { get; }

        /// <summary>
        /// The size of section headers.
        /// </summary>
        ushort SectionHeaderEntrySize { get; }

        /// <summary>
        /// The count of section headers.
        /// </summary>
        ushort SectionHeaderCount { get; }

        /// <summary>
        /// The section header string index.
        /// </summary>
        ushort SectionHeaderStringIndex { get; }
    }
}
