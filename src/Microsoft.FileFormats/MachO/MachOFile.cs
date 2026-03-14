// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.FileFormats.MachO
{
    public class MachOFatFile : IDisposable
    {
        private readonly Reader _reader;
        private readonly Lazy<MachFatHeaderMagic> _headerMagic;
        private readonly Lazy<Reader> _headerReader;
        private readonly Lazy<MachFatHeader> _header;
        private readonly Lazy<MachFatArch[]> _arches;
        private readonly Lazy<MachOFile[]> _archSpecificFiles;

        public MachOFatFile(IAddressSpace dataSource)
        {
            _reader = new Reader(dataSource);
            _headerMagic = new Lazy<MachFatHeaderMagic>(() => _reader.Read<MachFatHeaderMagic>(0));
            _headerReader = new Lazy<Reader>(() => new Reader(dataSource, new LayoutManager().AddMachFatHeaderTypes(IsBigEndian)));
            _header = new Lazy<MachFatHeader>(() => _headerReader.Value.Read<MachFatHeader>(0));
            _arches = new Lazy<MachFatArch[]>(ReadArches);
            _archSpecificFiles = new Lazy<MachOFile[]>(ReadArchSpecificFiles);
        }

        public MachFatHeaderMagic HeaderMagic { get { return _headerMagic.Value; } }
        public MachFatHeader Header { get { return _header.Value; } }
        public MachFatArch[] Arches { get { return _arches.Value; } }
        public MachOFile[] ArchSpecificFiles { get { return _archSpecificFiles.Value; } }

        public void Dispose()
        {
            if (_reader.DataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public bool IsValid()
        {
            if (_reader.Length > (_reader.SizeOf<MachFatHeaderMagic>()))
            {
                try
                {
                    return HeaderMagic.IsMagicValid.Check();
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException)
                {
                }
            }
            return false;
        }

        public bool IsBigEndian
        {
            get
            {
                HeaderMagic.IsMagicValid.CheckThrowing();
                return HeaderMagic.Magic == MachFatHeaderMagicKind.BigEndian;
            }
        }

        private MachFatArch[] ReadArches()
        {
            Header.IsCountFatArchesReasonable.CheckThrowing();
            ulong position = _headerReader.Value.SizeOf<MachFatHeader>();
            return _headerReader.Value.ReadArray<MachFatArch>(position, Header.CountFatArches);
        }

        private MachOFile[] ReadArchSpecificFiles()
        {
            return Arches.Select(a => new MachOFile(new RelativeAddressSpace(_reader.DataSource, a.Offset, a.Size))).ToArray();
        }
    }

    public class MachOFile : IDisposable
    {
        private readonly ulong _position;
        private readonly bool _dataSourceIsVirtualAddressSpace;
        private readonly Reader _reader;
        private readonly Lazy<MachHeaderMagic> _headerMagic;
        private readonly Lazy<Reader> _dataSourceReader;
        private readonly Lazy<MachHeader> _header;
        private readonly Lazy<Tuple<MachLoadCommand, ulong>[]> _loadCommands;
        private readonly Lazy<MachSegment[]> _segments;
        private readonly Lazy<MachSection[]> _sections;
        private readonly Lazy<Reader> _virtualAddressReader;
        private readonly Lazy<Reader> _physicalAddressReader;
        private readonly Lazy<byte[]> _uuid;
        private readonly Lazy<MachSymtab> _symtab;

        public MachOFile(IAddressSpace dataSource, ulong position = 0, bool dataSourceIsVirtualAddressSpace = false)
        {
            _position = position;
            _dataSourceIsVirtualAddressSpace = dataSourceIsVirtualAddressSpace;
            _reader = new Reader(dataSource);
            _headerMagic = new Lazy<MachHeaderMagic>(() => _reader.Read<MachHeaderMagic>(_position));
            _dataSourceReader = new Lazy<Reader>(CreateDataSourceReader);
            _header = new Lazy<MachHeader>(() => DataSourceReader.Read<MachHeader>(_position));
            _loadCommands = new Lazy<Tuple<MachLoadCommand, ulong>[]>(ReadLoadCommands);
            _segments = new Lazy<MachSegment[]>(ReadSegments);
            _sections = new Lazy<MachSection[]>(() => Segments.SelectMany(seg => seg.Sections).ToArray());
            _virtualAddressReader = new Lazy<Reader>(CreateVirtualReader);
            _physicalAddressReader = new Lazy<Reader>(CreatePhysicalReader);
            _uuid = new Lazy<byte[]>(ReadUuid);
            _symtab = new Lazy<MachSymtab>(ReadSymtab);
        }

        public MachHeaderMagic HeaderMagic { get { return _headerMagic.Value; } }
        public MachHeader Header { get { return _header.Value; } }
        public byte[] Uuid { get { return _uuid.Value; } }
        public MachSegment[] Segments { get { return _segments.Value; } }
        public MachSection[] Sections { get { return _sections.Value; } }
        public Reader VirtualAddressReader { get { return _virtualAddressReader.Value; } }
        public Reader PhysicalAddressReader { get { return _physicalAddressReader.Value; } }
        public MachSymtab Symtab { get { return _symtab.Value; } }
        private Reader DataSourceReader { get { return _dataSourceReader.Value; } }

        public void Dispose()
        {
            if (_reader.DataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        public bool IsValid()
        {
            if (_reader.Length > (_position + _reader.SizeOf<MachHeaderMagic>()))
            {
                try
                {
                    return HeaderMagic.IsMagicValid.Check();
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException)
                {
                }
            }
            return false;
        }

        public bool IsBigEndian
        {
            get
            {
                HeaderMagic.IsMagicValid.CheckThrowing();
                return (HeaderMagic.Magic == MachHeaderMagicType.BigEndian32Bit ||
                        HeaderMagic.Magic == MachHeaderMagicType.BigEndian64Bit);
            }
        }

        public bool Is64Bit
        {
            get
            {
                HeaderMagic.IsMagicValid.CheckThrowing();
                return (HeaderMagic.Magic == MachHeaderMagicType.LittleEndian64Bit ||
                        HeaderMagic.Magic == MachHeaderMagicType.BigEndian64Bit);
            }
        }

        public ulong PreferredVMBaseAddress
        {
            get
            {
                MachSegment first = Segments.Where(s => s.LoadCommand.SegName.ToString() == "__TEXT").FirstOrDefault();
                return first != null ? _position - first.LoadCommand.VMAddress : 0;
            }
        }

        public ulong LoadAddress
        {
            get
            {
                if (_dataSourceIsVirtualAddressSpace)
                {
                    return _position;
                }
                else
                {
                    return PreferredVMBaseAddress;
                }
            }
        }

        private Reader CreateDataSourceReader()
        {
            return new Reader(_reader.DataSource, new LayoutManager().AddMachTypes(IsBigEndian, Is64Bit));
        }

        private Reader CreateVirtualReader()
        {
            if (_dataSourceIsVirtualAddressSpace)
            {
                return DataSourceReader;
            }
            else
            {
                return DataSourceReader.WithAddressSpace(new MachVirtualAddressSpace(Segments));
            }
        }

        private Reader CreatePhysicalReader()
        {
            if (!_dataSourceIsVirtualAddressSpace)
            {
                return DataSourceReader;
            }
            else
            {
                return DataSourceReader.WithAddressSpace(new MachPhysicalAddressSpace(_reader.DataSource, PreferredVMBaseAddress, Segments));
            }
        }

        private Tuple<MachLoadCommand, ulong>[] ReadLoadCommands()
        {
            Header.IsNumberCommandsReasonable.CheckThrowing();
            ulong position = _position + DataSourceReader.SizeOf<MachHeader>();
            //TODO: do this more cleanly
            if (Is64Bit)
            {
                position += 4; // the 64 bit version has an extra padding field to align at an
                               // 8 byte boundary
            }
            List<Tuple<MachLoadCommand, ulong>> cmds = new();
            for (uint i = 0; i < Header.NumberCommands; i++)
            {
                MachLoadCommand cmd = DataSourceReader.Read<MachLoadCommand>(position);
                cmd.IsCmdSizeReasonable.CheckThrowing();
                cmds.Add(new Tuple<MachLoadCommand, ulong>(cmd, position));
                position += cmd.CommandSize;
            }

            return cmds.ToArray();
        }

        private byte[] ReadUuid()
        {
            IsAtLeastOneUuidLoadCommand.CheckThrowing();
            IsAtMostOneUuidLoadCommand.CheckThrowing();
            Tuple<MachLoadCommand, ulong> cmdAndPos = _loadCommands.Value.Where(c => c.Item1.Command == LoadCommandType.Uuid).First();
            MachUuidLoadCommand uuidCmd = DataSourceReader.Read<MachUuidLoadCommand>(cmdAndPos.Item2);
            uuidCmd.IsCommandSizeValid.CheckThrowing();
            return uuidCmd.Uuid;
        }

        private MachSegment[] ReadSegments()
        {
            List<MachSegment> segs = new();
            foreach (Tuple<MachLoadCommand, ulong> cmdAndPos in _loadCommands.Value)
            {
                LoadCommandType segType = Is64Bit ? LoadCommandType.Segment64 : LoadCommandType.Segment;
                if (cmdAndPos.Item1.Command != segType)
                {
                    continue;
                }
                MachSegment seg = new(DataSourceReader, cmdAndPos.Item2, _dataSourceIsVirtualAddressSpace);
                segs.Add(seg);
            }

            return segs.ToArray();
        }

        private MachSymtab ReadSymtab()
        {
            IsAtLeastOneSymtabLoadCommand.CheckThrowing();
            IsAtMostOneSymtabLoadCommand.CheckThrowing();
            ulong symtabPosition = 0;
            ulong dysymtabPosition = 0;
            foreach (Tuple<MachLoadCommand, ulong> cmdAndPos in _loadCommands.Value)
            {
                switch (cmdAndPos.Item1.Command)
                {
                    case LoadCommandType.Symtab:
                        symtabPosition = cmdAndPos.Item2;
                        break;
                    case LoadCommandType.DySymtab:
                        dysymtabPosition = cmdAndPos.Item2;
                        break;
                }
            }
            if (symtabPosition == 0 || dysymtabPosition == 0)
            {
                return null;
            }
            return new MachSymtab(DataSourceReader, symtabPosition, dysymtabPosition, PhysicalAddressReader);
        }

        #region Validation Rules
        public ValidationRule IsAtMostOneUuidLoadCommand
        {
            get
            {
                return new ValidationRule("Mach load command sequence has too many uuid elements",
                                          () => _loadCommands.Value.Count(c => c.Item1.Command == LoadCommandType.Uuid) <= 1);
            }
        }
        public ValidationRule IsAtLeastOneUuidLoadCommand
        {
            get
            {
                return new ValidationRule("Mach load command sequence has no uuid elements",
                                          () => _loadCommands.Value.Any(c => c.Item1.Command == LoadCommandType.Uuid));
            }
        }
        public ValidationRule IsAtMostOneSymtabLoadCommand
        {
            get
            {
                return new ValidationRule("Mach load command sequence has too many symtab elements",
                                          () => _loadCommands.Value.Count(c => c.Item1.Command == LoadCommandType.Symtab) <= 1);
            }
        }
        public ValidationRule IsAtLeastOneSymtabLoadCommand
        {
            get
            {
                return new ValidationRule("Mach load command sequence has no symtab elements",
                                          () => _loadCommands.Value.Any(c => c.Item1.Command == LoadCommandType.Symtab));
            }
        }
        public ValidationRule IsAtLeastOneSegmentAtFileOffsetZero
        {
            get
            {
                return new ValidationRule("Mach load command sequence has no segments which contain file offset zero",
                                          () => Segments.Where(s => s.LoadCommand.FileOffset == 0 &&
                                                                    s.LoadCommand.FileSize != 0).Any());
            }
        }
        #endregion
    }

    public class MachSegment
    {
        private readonly Reader _dataSourceReader;
        private readonly ulong _position;
        private readonly bool _readerIsVirtualAddressSpace;
        private readonly Lazy<MachSegmentLoadCommand> _loadCommand;
        private readonly Lazy<MachSection[]> _sections;
        private readonly Lazy<Reader> _physicalContents;
        private readonly Lazy<Reader> _virtualContents;

        public MachSegment(Reader machReader, ulong position, bool readerIsVirtualAddressSpace = false)
        {
            _dataSourceReader = machReader;
            _position = position;
            _readerIsVirtualAddressSpace = readerIsVirtualAddressSpace;
            _loadCommand = new Lazy<MachSegmentLoadCommand>(() => _dataSourceReader.Read<MachSegmentLoadCommand>(_position));
            _sections = new Lazy<MachSection[]>(ReadSections);
            _physicalContents = new Lazy<Reader>(CreatePhysicalSegmentAddressSpace);
            _virtualContents = new Lazy<Reader>(CreateVirtualSegmentAddressSpace);
        }

        public MachSegmentLoadCommand LoadCommand { get { return _loadCommand.Value; } }
        public IEnumerable<MachSection> Sections { get { return _sections.Value; } }
        public Reader PhysicalContents { get { return _physicalContents.Value; } }
        public Reader VirtualContents { get { return _virtualContents.Value; } }

        private MachSection[] ReadSections()
        {
            ulong sectionStartOffset = _position + _dataSourceReader.SizeOf<MachSegmentLoadCommand>();
            return _dataSourceReader.ReadArray<MachSection>(sectionStartOffset, _loadCommand.Value.CountSections);
        }

        private Reader CreatePhysicalSegmentAddressSpace()
        {
            if (!_readerIsVirtualAddressSpace)
            {
                return _dataSourceReader.WithRelativeAddressSpace(LoadCommand.FileOffset, LoadCommand.FileSize, 0);
            }
            else
            {
                return _dataSourceReader.WithRelativeAddressSpace(LoadCommand.VMAddress, LoadCommand.FileSize,
                                                            (long)(LoadCommand.FileOffset - LoadCommand.VMAddress));
            }
        }

        private Reader CreateVirtualSegmentAddressSpace()
        {
            if (_readerIsVirtualAddressSpace)
            {
                return _dataSourceReader.WithRelativeAddressSpace(LoadCommand.VMAddress, LoadCommand.VMSize, 0);
            }
            else
            {
                return _dataSourceReader.WithAddressSpace(
                    new PiecewiseAddressSpace(
                        new PiecewiseAddressSpaceRange()
                        {
                            AddressSpace = new RelativeAddressSpace(_dataSourceReader.DataSource, LoadCommand.FileOffset, LoadCommand.FileSize,
                                                                             (long)(LoadCommand.VMAddress - LoadCommand.FileOffset)),
                            Start = LoadCommand.VMAddress,
                            Length = LoadCommand.FileSize
                        },
                        new PiecewiseAddressSpaceRange()
                        {
                            AddressSpace = new ZeroAddressSpace(LoadCommand.VMAddress + LoadCommand.VMSize),
                            Start = LoadCommand.VMAddress + LoadCommand.FileSize,
                            Length = LoadCommand.VMSize - LoadCommand.FileSize
                        }));
            }
        }
    }


    public class MachVirtualAddressSpace : PiecewiseAddressSpace
    {
        public MachVirtualAddressSpace(IEnumerable<MachSegment> segments) : base(segments.Select(s => ToRange(s)).ToArray())
        {
        }

        private static PiecewiseAddressSpaceRange ToRange(MachSegment segment)
        {
            return new PiecewiseAddressSpaceRange()
            {
                AddressSpace = segment.VirtualContents.DataSource,
                Start = segment.LoadCommand.VMAddress,
                Length = segment.LoadCommand.VMSize
            };
        }
    }

    public class MachPhysicalAddressSpace : PiecewiseAddressSpace
    {
        public MachPhysicalAddressSpace(IAddressSpace virtualAddressSpace, ulong preferredVMBaseAddress, IEnumerable<MachSegment> segments) :
            base(segments.Select(s => ToRange(virtualAddressSpace, preferredVMBaseAddress, s)).ToArray())
        {
        }

        private static PiecewiseAddressSpaceRange ToRange(IAddressSpace virtualAddressSpace, ulong preferredVMBaseAddress, MachSegment segment)
        {
            ulong actualSegmentLoadAddress = preferredVMBaseAddress + segment.LoadCommand.VMAddress - segment.LoadCommand.FileOffset;
            return new PiecewiseAddressSpaceRange()
            {
                AddressSpace = new RelativeAddressSpace(virtualAddressSpace, actualSegmentLoadAddress, segment.LoadCommand.FileSize),
                Start = segment.LoadCommand.FileOffset,
                Length = segment.LoadCommand.FileSize
            };
        }
    }

    public class MachSymbol
    {
        public string Name;
        public ulong Value { get { return Raw.Value; } }
        public NList Raw;

        public override string ToString()
        {
            return Name + "@0x" + Value.ToString("x");
        }
    }

    public class MachSymtab
    {
        private readonly Reader _machReader;
        private readonly Reader _physicalAddressSpace;
        private readonly Lazy<MachSymtabLoadCommand> _symtabLoadCommand;
        private readonly Lazy<MachDySymtabLoadCommand> _dysymtabLoadCommand;
        private readonly Lazy<MachSymbol[]> _symbols;
        private readonly Lazy<Reader> _stringReader;
        private readonly Lazy<NList[]> _symbolTable;

        public MachSymtab(Reader machReader, ulong symtabPosition, ulong dysymtabPosition, Reader physicalAddressSpace)
        {
            _machReader = machReader;
            _physicalAddressSpace = physicalAddressSpace;
            _symtabLoadCommand = new Lazy<MachSymtabLoadCommand>(() => _machReader.Read<MachSymtabLoadCommand>(symtabPosition));
            _dysymtabLoadCommand = new Lazy<MachDySymtabLoadCommand>(() => _machReader.Read<MachDySymtabLoadCommand>(dysymtabPosition));
            _stringReader = new Lazy<Reader>(GetStringReader);
            _symbolTable = new Lazy<NList[]>(ReadSymbolTable);
            _symbols = new Lazy<MachSymbol[]>(ReadSymbols);
        }

        public IEnumerable<MachSymbol> Symbols { get { return _symbols.Value; } }

        public bool TryLookupSymbol(string symbol, out ulong offset)
        {
            if (symbol is null)
            {
                throw new ArgumentNullException(nameof(symbol));
            }
            MachSymtabLoadCommand symtabLoadCommand = _symtabLoadCommand.Value;
            MachDySymtabLoadCommand dysymtabLoadCommand = _dysymtabLoadCommand.Value;

            // First, search just the "external" export symbols
            if (TryLookupSymbol(dysymtabLoadCommand.IExtDefSym, dysymtabLoadCommand.NextDefSym, symbol, out offset))
            {
                return true;
            }

            // If not found in external symbols, search all of them
            if (TryLookupSymbol(0, symtabLoadCommand.SymCount, symbol, out offset))
            {
                return true;
            }

            offset = 0;
            return false;
        }

        private bool TryLookupSymbol(uint start, uint nsyms, string symbol, out ulong offset)
        {
            NList[] symTable = _symbolTable.Value;
            if (symTable is not null)
            {
                for (uint i = 0; i < nsyms && start + i < symTable.Length; i++)
                {
                    string name = _stringReader.Value.Read<string>(symTable[start + i].StringIndex);
                    if (name.Length > 0)
                    {
                        // Skip the leading underscores to match Linux externs
                        if (name[0] == '_')
                        {
                            name = name.Substring(1);
                        }
                        if (name == symbol)
                        {
                            offset = symTable[start + i].Value;
                            return true;
                        }
                    }
                }
            }
            offset = 0;
            return false;
        }

        private MachSymbol[] ReadSymbols()
        {
            Reader stringReader = _stringReader.Value;
            return _symbolTable.Value?.Select(n => new MachSymbol() { Name = stringReader.Read<string>(n.StringIndex), Raw = n }).ToArray();
        }

        private Reader GetStringReader()
        {
            MachSymtabLoadCommand symtabLoadCommand = _symtabLoadCommand.Value;
            return _physicalAddressSpace.WithRelativeAddressSpace(symtabLoadCommand.StringOffset, symtabLoadCommand.StringSize);
        }

        private NList[] ReadSymbolTable()
        {
            MachSymtabLoadCommand symtabLoadCommand = _symtabLoadCommand.Value;
            if (symtabLoadCommand.IsNSymsReasonable.Check() && symtabLoadCommand.SymOffset > 0)
            {
                return _physicalAddressSpace.ReadArray<NList>(symtabLoadCommand.SymOffset, symtabLoadCommand.SymCount);
            }
            return null;
        }
    }
}
