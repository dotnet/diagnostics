// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.Diagnostics.Runtime.MacOS.Structs;

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal sealed unsafe class MachOModule
    {
        public MachOCoreDump? Parent { get; }

        public ulong BaseAddress { get; }
        public ulong ImageSize { get; }
        public string FileName { get; }
        public ulong LoadBias { get; }
        public ImmutableArray<byte> BuildId { get; }

        public IDataReader DataReader { get; }

        private readonly MachHeader64 _header;
        private readonly SymtabLoadCommand _symtab;
        private readonly DysymtabLoadCommand _dysymtab;
        private readonly MachOSegment[] _segments;
        private readonly ulong _stringTableAddress;
        private volatile NList64[]? _symTable;

        public MachOModule(IDataReader reader, ulong address, string path)
            : this(null, reader, reader.Read<MachHeader64>(address), address, path)
        {
        }

        public MachOModule(MachOCoreDump parent, ulong address, string path)
            : this(parent, parent.Parent, parent.ReadMemory<MachHeader64>(address), address, path)
        {
        }

        private MachOModule(MachOCoreDump? parent, IDataReader reader, in MachHeader64 header, ulong address, string path)
        {
            BaseAddress = address;
            FileName = path;
            Parent = parent;
            _header = header;
            DataReader = reader;

            if (header.Magic != MachHeader64.Magic64)
                throw new InvalidDataException($"Module at {address:x} does not contain the expected Mach-O header.");

            // Since MachO segments are not contiguous the image size is just the headers/commands
            ImageSize = MachHeader64.Size + _header.SizeOfCommands;

            List<MachOSegment> segments = new((int)_header.NumberCommands);

            uint offset = (uint)sizeof(MachHeader64);
            for (int i = 0; i < _header.NumberCommands; i++)
            {
                ulong cmdAddress = BaseAddress + offset;
                LoadCommandHeader cmd = DataReader.Read<LoadCommandHeader>(cmdAddress);

                switch (cmd.Kind)
                {
                    case LoadCommandType.Segment64:
                        Segment64LoadCommand seg64LoadCmd = DataReader.Read<Segment64LoadCommand>(cmdAddress + (uint)sizeof(LoadCommandHeader));
                        segments.Add(new MachOSegment(seg64LoadCmd));
                        if (seg64LoadCmd.Name.Equals("__TEXT"))
                        {
                            LoadBias = BaseAddress - seg64LoadCmd.VMAddr;
                        }
                        break;

                    case LoadCommandType.SymTab:
                        _symtab = DataReader.Read<SymtabLoadCommand>(cmdAddress);
                        break;

                    case LoadCommandType.DysymTab:
                        _dysymtab = DataReader.Read<DysymtabLoadCommand>(cmdAddress);
                        break;

                    case LoadCommandType.Uuid:
                        UuidLoadCommand uuid = DataReader.Read<UuidLoadCommand>(cmdAddress);
                        if (uuid.Header.Kind == LoadCommandType.Uuid)
                        {
                            BuildId = uuid.BuildId.ToImmutableArray();
                        }
                        break;
                }

                offset += cmd.Size;
            }

            segments.Sort((x, y) => x.Address.CompareTo(y.Address));
            _segments = segments.ToArray();

            if (_symtab.StrOff != 0)
                _stringTableAddress = GetAddressFromFileOffset(_symtab.StrOff);
        }

        public bool TryLookupSymbol(string symbol, out ulong address)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            if (_stringTableAddress != 0)
            {
                // First, search just the "external" export symbols
                if (TryLookupSymbol(_dysymtab.iextdefsym, _dysymtab.nextdefsym, symbol, out address))
                {
                    return true;
                }

                // If not found in external symbols, search all of them
                if (TryLookupSymbol(0, _symtab.NSyms, symbol, out address))
                {
                    return true;
                }
            }

            address = 0;
            return false;
        }

        private bool TryLookupSymbol(uint start, uint nsyms, string symbol, out ulong address)
        {
            address = 0;

            NList64[]? symTable = ReadSymbolTable();
            if (symTable is null)
                return false;

            for (uint i = 0; i < nsyms && start + i < symTable.Length; i++)
            {
                string name = GetSymbolName(symTable[start + i], symbol.Length + 1);
                if (name.Length > 0)
                {
                    // Skip the leading underscores to match Linux externs
                    if (name[0] == '_')
                    {
                        name = name.Substring(1);
                    }
                    if (name == symbol)
                    {
                        address = LoadBias + symTable[start + i].n_value;
                        return true;
                    }
                }
            }

            return false;
        }

        private string GetSymbolName(NList64 tableEntry, int max)
        {
            ulong nameOffset = _stringTableAddress + tableEntry.n_strx;
            return ReadAscii(nameOffset, max);
        }

        internal string ReadAscii(ulong address, int max)
        {
            Span<byte> buffer = new byte[max];
            int count = DataReader.Read(address, buffer);
            if (count == 0)
                return "";

            buffer = buffer.Slice(0, count);
            if (buffer[buffer.Length - 1] == 0)
                buffer = buffer.Slice(0, buffer.Length - 1);
            string result = Encoding.ASCII.GetString(buffer);
            return result;
        }

        private NList64[]? ReadSymbolTable()
        {
            if (_symTable != null)
                return _symTable;

            if (_dysymtab.Header.Kind != LoadCommandType.DysymTab || _symtab.Header.Kind != LoadCommandType.SymTab)
                return null;

            ulong symbolTableAddress = GetAddressFromFileOffset(_symtab.SymOff);
            NList64[] symTable = new NList64[_symtab.NSyms];

            int count;
            fixed (NList64* ptr = symTable)
                count = DataReader.Read(symbolTableAddress, new Span<byte>(ptr, symTable.Length * sizeof(NList64))) / sizeof(NList64);

            _symTable = symTable;
            return symTable;
        }

        private ulong GetAddressFromFileOffset(uint fileOffset)
        {
            foreach (MachOSegment seg in _segments)
                if (seg.FileOffset <= fileOffset && fileOffset < seg.FileOffset + seg.FileSize)
                    return LoadBias + fileOffset + seg.Address - seg.FileOffset;

            return LoadBias + fileOffset;
        }

        public IEnumerable<Segment64LoadCommand> EnumerateSegments()
        {
            uint offset = MachHeader64.Size;
            for (int i = 0; i < _header.NumberCommands; i++)
            {
                ulong cmdAddress = BaseAddress + offset;
                LoadCommandHeader cmd = DataReader.Read<LoadCommandHeader>(cmdAddress);

                if (cmd.Kind == LoadCommandType.Segment64)
                    yield return DataReader.Read<Segment64LoadCommand>(cmdAddress + LoadCommandHeader.HeaderSize);

                offset += cmd.Size;
            }
        }
    }
}