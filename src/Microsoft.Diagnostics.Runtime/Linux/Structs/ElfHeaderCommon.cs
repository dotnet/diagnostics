// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct ElfHeaderCommon
    {
        private const int EI_NIDENT = 16;

        internal const uint Magic = 0x464c457f;

        private readonly uint _magic;
        private readonly byte _class;
        private readonly byte _data;

        private readonly byte _unused0;
        private readonly byte _unused1;
        private readonly uint _unused2;
        private readonly uint _unused3;

        private readonly ElfHeaderType _type;
        private readonly ushort _machine;
        private readonly uint _version;

        public bool IsValid
        {
            get
            {
                if (_magic != Magic)
                    return false;

                return Data == ElfData.LittleEndian;
            }
        }

        public ElfHeaderType Type => _type;

        public ElfMachine Architecture => (ElfMachine)_machine;

        public ElfClass Class => (ElfClass)_class;

        public ElfData Data => (ElfData)_data;

        public IElfHeader? GetHeader(Reader reader, ulong position)
        {
            if (IsValid)
            {
                switch (Architecture)
                {
                    case ElfMachine.EM_X86_64:
                    case ElfMachine.EM_AARCH64:
                        return reader.Read<ElfHeader64>(position);

                    case ElfMachine.EM_386:
                    case ElfMachine.EM_ARM:
                        return reader.Read<ElfHeader32>(position);
                }
            }

            return null;
        }
    }
}