// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed class ElfSymbolTable
    {
        private readonly Reader _reader;
        private readonly ulong _address;
        private readonly bool _is64Bit;
        private readonly ElfStringTable _stringTable;
        private readonly uint _symSize;

        public ElfSymbolTable(Reader reader, bool is64Bit, ulong address, ElfStringTable stringTable)
        {
            _reader = reader;
            _address = address;
            _is64Bit = is64Bit;
            _symSize = is64Bit ? 24u : 16u;
            _stringTable = stringTable;
        }

        public ElfSymbol GetSymbol(uint index)
        {
            if (_is64Bit)
            {
                ElfSymbol64 s = _reader.Read<ElfSymbol64>(_address + index * _symSize);
                string name = _stringTable.GetStringAtIndex(s.Name);
                ElfSymbolBind b = (ElfSymbolBind)(s.Info >> 4);
                ElfSymbolType t = (ElfSymbolType)(s.Info & 0xF);
                return new ElfSymbol(name, b, t, s.Value, (long)s.Size);
            }
            else
            {
                ElfSymbol32 s = _reader.Read<ElfSymbol32>(_address + index * _symSize);
                string name = _stringTable.GetStringAtIndex(s.Name);
                ElfSymbolBind b = (ElfSymbolBind)(s.Info >> 4);
                ElfSymbolType t = (ElfSymbolType)(s.Info & 0xF);
                return new ElfSymbol(name, b, t, (long)s.Value, s.Size);
            }
        }

        internal static ElfSymbolTable? Create(Reader reader, bool is64Bit, ulong symbolTableVA, ElfStringTable? stringTable)
        {
            if (symbolTableVA == 0 || stringTable is null)
                return null;

            try
            {
                return new ElfSymbolTable(reader, is64Bit, symbolTableVA, stringTable);
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }

            return null;
        }
    }
}