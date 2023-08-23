// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct ElfHeader64 : IElfHeader
    {
        private readonly ElfHeaderCommon _common;

        private readonly ulong _entry;
        private readonly ulong _programHeaderOffset;
        private readonly ulong _sectionHeaderOffset;

        private readonly uint _flags;
        private readonly ushort _ehSize;
        private readonly ushort _programHeaderEntrySize;
        private readonly ushort _programHeaderCount;
        private readonly ushort _sectionHeaderEntrySize;
        private readonly ushort _sectionHeaderCount;
        private readonly ushort _sectionHeaderStringIndex;

        #region IElfHeader

        public bool Is64Bit => true;

        public bool IsValid => _common.IsValid;

        public ElfHeaderType Type => _common.Type;

        public ElfMachine Architecture => _common.Architecture;

        public ulong ProgramHeaderOffset => _programHeaderOffset;

        public ulong SectionHeaderOffset => _sectionHeaderOffset;

        public ushort ProgramHeaderEntrySize => _programHeaderEntrySize;

        public ushort ProgramHeaderCount => _programHeaderCount;

        public ushort SectionHeaderEntrySize => _sectionHeaderEntrySize;

        public ushort SectionHeaderCount => _sectionHeaderCount;

        public ushort SectionHeaderStringIndex => _sectionHeaderStringIndex;

        #endregion
    }
}