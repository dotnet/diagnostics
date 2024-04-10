// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.FileFormats.ELF
{
    public class ELFFile : IDisposable
    {
        private readonly ulong _position;
        private readonly bool _isDataSourceVirtualAddressSpace;
        private readonly Reader _reader;
        private readonly Lazy<ELFHeaderIdent> _ident;
        private readonly Lazy<Reader> _dataSourceReader;
        private readonly Lazy<ELFHeader> _header;
        private readonly Lazy<IEnumerable<ELFProgramSegment>> _segments;
        private readonly Lazy<ELFSection[]> _sections;
        private readonly Lazy<Reader> _virtualAddressReader;
        private readonly Lazy<byte[]> _buildId;
        private readonly Lazy<byte[]> _sectionNameTable;

        public ELFFile(IAddressSpace dataSource, ulong position = 0, bool isDataSourceVirtualAddressSpace = false)
        {
            _position = position;
            _reader = new Reader(dataSource);
            _isDataSourceVirtualAddressSpace = isDataSourceVirtualAddressSpace;
            _ident = new Lazy<ELFHeaderIdent>(() => _reader.Read<ELFHeaderIdent>(_position));
            _dataSourceReader = new Lazy<Reader>(() => new Reader(dataSource, new LayoutManager().AddELFTypes(IsBigEndian, Is64Bit)));
            _header = new Lazy<ELFHeader>(() => DataSourceReader.Read<ELFHeader>(_position));
            _segments = new Lazy<IEnumerable<ELFProgramSegment>>(ReadSegments);
            _sections = new Lazy<ELFSection[]>(ReadSections);
            _virtualAddressReader = new Lazy<Reader>(CreateVirtualAddressReader);
            _buildId = new Lazy<byte[]>(ReadBuildId);
            _sectionNameTable = new Lazy<byte[]>(ReadSectionNameTable);
        }

        public ELFHeaderIdent Ident { get { return _ident.Value; } }
        public ELFHeader Header { get { return _header.Value; } }
        private Reader DataSourceReader { get { return _dataSourceReader.Value; } }
        public IEnumerable<ELFProgramSegment> Segments { get { return _segments.Value; } }
        public ELFSection[] Sections { get { return _sections.Value; } }
        public Reader VirtualAddressReader { get { return _virtualAddressReader.Value; } }
        public byte[] BuildID { get { return _buildId.Value; } }
        public byte[] SectionNameTable { get { return _sectionNameTable.Value; } }

        public void Dispose()
        {
            if (_reader.DataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public bool IsValid()
        {
            if (_reader.Length > (_position + _reader.SizeOf<ELFHeaderIdent>()))
            {
                try
                {
                    return Ident.IsIdentMagicValid.Check();
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
                Ident.IsIdentMagicValid.CheckThrowing();
                Ident.IsDataValid.CheckThrowing();
                return Ident.Data == ELFData.BigEndian;
            }
        }

        public bool Is64Bit
        {
            get
            {
                Ident.IsIdentMagicValid.CheckThrowing();
                Ident.IsClassValid.CheckThrowing();
                return (Ident.Class == ELFClass.Class64);
            }
        }

        public ulong PreferredVMBaseAddress
        {
            get
            {
                ulong minAddr = ulong.MaxValue;

                foreach (ELFProgramSegment segment in Segments)
                {
                    if (segment.Header.Type == ELFProgramHeaderType.Load)
                    {
                        minAddr = Math.Min(minAddr, segment.Header.VirtualAddress);
                    }
                }

                return minAddr;
            }
        }

        public ELFSection FindSectionByName(string name)
        {
            foreach (ELFSection section in Sections)
            {
                if (string.Equals(section.Name, name))
                {
                    return section;
                }
            }
            return null;
        }

        private IEnumerable<ELFProgramSegment> ReadSegments()
        {
            Header.IsProgramHeaderCountReasonable.CheckThrowing();
            IsHeaderProgramHeaderOffsetValid.CheckThrowing();
            IsHeaderProgramHeaderEntrySizeValid.CheckThrowing();

            // Calculate the loadBias. It is usually just the base address except for some executable modules.
            ulong loadBias = _position;
            if (loadBias > 0)
            {
                for (uint i = 0; i < Header.ProgramHeaderCount; i++)
                {
                    ulong programHeaderOffset = _position + Header.ProgramHeaderOffset + i * Header.ProgramHeaderEntrySize;
                    ELFProgramHeader header = DataSourceReader.Read<ELFProgramHeader>(programHeaderOffset);
                    if (header.Type == ELFProgramHeaderType.Load && header.FileOffset == 0)
                    {
                        loadBias -= header.VirtualAddress;
                    }
                }
            }

            // Build the program segments
            List<ELFProgramSegment> segments = new();
            for (uint i = 0; i < Header.ProgramHeaderCount; i++)
            {
                ulong programHeaderOffset = _position + Header.ProgramHeaderOffset + i * Header.ProgramHeaderEntrySize;
                segments.Add(new ELFProgramSegment(DataSourceReader, loadBias, programHeaderOffset, _isDataSourceVirtualAddressSpace));
            }
            return segments;
        }

        private ELFSection[] ReadSections()
        {
            Header.IsSectionHeaderCountReasonable.CheckThrowing();
            IsHeaderSectionHeaderOffsetValid.CheckThrowing();
            IsHeaderSectionHeaderEntrySizeValid.CheckThrowing();

            List<ELFSection> sections = new();
            for (uint i = 0; i < Header.SectionHeaderCount; i++)
            {
                sections.Add(new ELFSection(this, DataSourceReader, _position, _position + Header.SectionHeaderOffset + i * Header.SectionHeaderEntrySize));
            }
            return sections.ToArray();
        }

        private Reader CreateVirtualAddressReader()
        {
            if (_isDataSourceVirtualAddressSpace)
            {
                return DataSourceReader;
            }
            else
            {
                return DataSourceReader.WithAddressSpace(new ELFVirtualAddressSpace(Segments));
            }
        }

        private byte[] ReadBuildId()
        {
            byte[] buildId = null;

            if (Header.ProgramHeaderOffset > 0 && Header.ProgramHeaderEntrySize > 0 && Header.ProgramHeaderCount > 0)
            {
                try
                {
                    foreach (ELFProgramSegment segment in Segments)
                    {
                        if (segment.Header.Type == ELFProgramHeaderType.Note)
                        {
                            buildId = ReadBuildIdNote(segment.Contents);
                            if (buildId != null)
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is OverflowException)
                {
                }
            }

            if (buildId == null)
            {
                // Use sections to find build id if there isn't any program headers (i.e. some FreeBSD .dbg files)
                try
                {
                    foreach (ELFSection section in Sections)
                    {
                        if (section.Header.Type == ELFSectionHeaderType.Note)
                        {
                            buildId = ReadBuildIdNote(section.Contents);
                            if (buildId != null)
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is OverflowException)
                {
                }
            }

            return buildId;
        }

        private static byte[] ReadBuildIdNote(Reader noteReader)
        {
            if (noteReader != null)
            {
                ELFNoteList noteList = new(noteReader);
                foreach (ELFNote note in noteList.Notes)
                {
                    ELFNoteType type = note.Header.Type;
                    if (type == ELFNoteType.GnuBuildId && note.Name.Equals("GNU"))
                    {
                        return note.Contents.Read(0, (uint)note.Contents.Length);
                    }
                }
            }
            return null;
        }

        private byte[] ReadSectionNameTable()
        {
            try
            {
                int nameTableIndex = Header.SectionHeaderStringIndex;
                if (Header.SectionHeaderOffset != 0 && Header.SectionHeaderCount > 0 && nameTableIndex != 0)
                {
                    ELFSection nameTableSection = Sections[nameTableIndex];
                    if (nameTableSection.Header.FileOffset > 0 && nameTableSection.Header.FileSize > 0)
                    {
                        return nameTableSection.Contents.Read(0, (uint)nameTableSection.Contents.Length);
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is OverflowException)
            {
            }
            return null;
        }

        #region Validation Rules

        public ValidationRule IsHeaderProgramHeaderOffsetValid
        {
            get
            {
                return new ValidationRule("ELF Header ProgramHeaderOffset is invalid or elf file is incomplete", () =>
                {
                    return Header.ProgramHeaderOffset < _reader.Length &&
                        Header.ProgramHeaderOffset + (ulong)(Header.ProgramHeaderEntrySize * Header.ProgramHeaderCount) <= _reader.Length;
                },
                IsHeaderProgramHeaderEntrySizeValid,
                Header.IsProgramHeaderCountReasonable);

            }
        }

        public ValidationRule IsHeaderProgramHeaderEntrySizeValid
        {
            get { return new ValidationRule("ELF Header ProgramHeaderEntrySize is invalid", () => Header.ProgramHeaderEntrySize == DataSourceReader.SizeOf<ELFProgramHeader>()); }
        }

        public ValidationRule IsHeaderSectionHeaderOffsetValid
        {
            get
            {
                return new ValidationRule("ELF Header SectionHeaderOffset is invalid or elf file is incomplete", () => {
                    return Header.SectionHeaderOffset < _reader.Length &&
                        Header.SectionHeaderOffset + (ulong)(Header.SectionHeaderEntrySize * Header.SectionHeaderCount) <= _reader.Length;
                },
                IsHeaderSectionHeaderEntrySizeValid,
                Header.IsSectionHeaderCountReasonable);
            }
        }

        public ValidationRule IsHeaderSectionHeaderEntrySizeValid
        {
            get { return new ValidationRule("ELF Header SectionHeaderEntrySize is invalid", () => Header.SectionHeaderEntrySize == DataSourceReader.SizeOf<ELFSectionHeader>()); }
        }

        #endregion
    }

    public class ELFVirtualAddressSpace : IAddressSpace
    {
        private readonly ELFProgramSegment[] _segments;

        public ELFVirtualAddressSpace(IEnumerable<ELFProgramSegment> segments)
        {
            _segments = segments.Where((programHeader) => programHeader.Header.FileSize > 0).ToArray();
            Length = _segments.Max(s => s.Header.VirtualAddress + s.Header.VirtualSize);
        }

        public ulong Length { get; private set; }

        public uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            uint bytesRead = 0;
            while (bytesRead != count)
            {
                int i = 0;
                for (; i < _segments.Length; i++)
                {
                    ELFProgramHeader header = _segments[i].Header;

                    ulong upperAddress = header.VirtualAddress + header.VirtualSize;
                    if (header.VirtualAddress <= position && position < upperAddress)
                    {
                        uint bytesToReadRange = (uint)Math.Min(count - bytesRead, upperAddress - position);
                        ulong segmentOffset = position - header.VirtualAddress;
                        uint bytesReadRange = _segments[i].Contents.Read(segmentOffset, buffer, bufferOffset, bytesToReadRange);
                        if (bytesReadRange == 0) {
                            goto done;
                        }
                        position += bytesReadRange;
                        bufferOffset += bytesReadRange;
                        bytesRead += bytesReadRange;
                        break;
                    }
                }
                if (i == _segments.Length) {
                    break;
                }
            }
        done:
            if (bytesRead == 0) {
                throw new InvalidVirtualAddressException(string.Format("Virtual address range is not mapped {0:X16} {1}", position, count));
            }
            // Zero the rest of the buffer if read less than requested
            Array.Clear(buffer, (int)bufferOffset, (int)(count - bytesRead));
            return bytesRead;
        }
    }

    public class ELFProgramSegment
    {
        private readonly Lazy<Reader> _contents;

        public ELFProgramSegment(Reader dataSourceReader, ulong elfOffset, ulong programHeaderOffset, bool isDataSourceVirtualAddressSpace)
        {
            Header = dataSourceReader.Read<ELFProgramHeader>(programHeaderOffset);
            if (isDataSourceVirtualAddressSpace)
            {
                _contents = new Lazy<Reader>(() => dataSourceReader.WithRelativeAddressSpace(elfOffset + Header.VirtualAddress, Header.VirtualSize));
            }
            else
            {
                _contents = new Lazy<Reader>(() => dataSourceReader.WithRelativeAddressSpace(elfOffset + Header.FileOffset, Header.FileSize));
            }
        }

        public ELFProgramHeader Header { get; }
        public Reader Contents { get { return _contents.Value; } }

        public override string ToString()
        {
            return "Segment@[" + Header.VirtualAddress.ToString() + "-" + (Header.VirtualAddress + Header.VirtualSize).ToString("x") + ")";
        }
    }

    public class ELFSection
    {
        private readonly ELFFile _elfFile;
        private readonly Reader _dataSourceReader;
        private readonly Lazy<ELFSectionHeader> _header;
        private readonly Lazy<string> _name;
        private readonly Lazy<Reader> _contents;

        private static readonly ASCIIEncoding _decoder = new();

        public ELFSection(ELFFile elfFile, Reader dataSourceReader, ulong elfOffset, ulong sectionHeaderOffset)
        {
            _elfFile = elfFile;
            _dataSourceReader = dataSourceReader;
            _header = new Lazy<ELFSectionHeader>(() => _dataSourceReader.Read<ELFSectionHeader>(sectionHeaderOffset));
            _name = new Lazy<string>(ReadName);
            _contents = new Lazy<Reader>(() => _dataSourceReader.WithRelativeAddressSpace(elfOffset + Header.FileOffset, Header.FileSize));
        }

        public ELFSectionHeader Header { get { return _header.Value; } }
        public string Name { get { return _name.Value; } }
        public Reader Contents { get { return _contents.Value; } }

        private string ReadName()
        {
            if (Header.Type == ELFSectionHeaderType.Null)
            {
                return string.Empty;
            }
            byte[] sectionNameTable = _elfFile.SectionNameTable;
            if (sectionNameTable == null || sectionNameTable.Length == 0)
            {
                return string.Empty;
            }
            if (Header.NameIndex > sectionNameTable.Length)
            {
                return string.Empty;
            }
            int index = (int)Header.NameIndex;
            if (index == 0)
            {
                return string.Empty;
            }
            int count = 0;
            for (; (index + count) < sectionNameTable.Length; count++)
            {
                if (sectionNameTable[index + count] == 0)
                {
                    break;
                }
            }
            return _decoder.GetString(sectionNameTable, index, count);
        }
    }

    public class ELFNoteList
    {
        private readonly Reader _elfSegmentReader;
        private readonly Lazy<IEnumerable<ELFNote>> _notes;

        public ELFNoteList(Reader elfSegmentReader)
        {
            _elfSegmentReader = elfSegmentReader;
            _notes = new Lazy<IEnumerable<ELFNote>>(ReadNotes);
        }

        public IEnumerable<ELFNote> Notes { get { return _notes.Value; } }

        private IEnumerable<ELFNote> ReadNotes()
        {
            List<ELFNote> notes = new();
            ulong position = 0;
            while (position < _elfSegmentReader.Length)
            {
                ELFNote note = new(_elfSegmentReader, position);
                notes.Add(note);
                position += note.Size;
            }
            return notes;
        }
    }

    public class ELFNote
    {
        private readonly Reader _elfSegmentReader;
        private readonly ulong _noteHeaderOffset;
        private readonly Lazy<ELFNoteHeader> _header;
        private readonly Lazy<string> _name;
        private readonly Lazy<Reader> _contents;

        public ELFNote(Reader elfSegmentReader, ulong offset)
        {
            _elfSegmentReader = elfSegmentReader;
            _noteHeaderOffset = offset;
            _header = new Lazy<ELFNoteHeader>(() => _elfSegmentReader.Read<ELFNoteHeader>(_noteHeaderOffset));
            _name = new Lazy<string>(ReadName);
            _contents = new Lazy<Reader>(CreateContentsReader);
        }

        public ELFNoteHeader Header { get { return _header.Value; } }
        //TODO: validate these fields
        public uint Size { get { return HeaderSize + Align4(Header.NameSize) + Align4(Header.ContentSize); } }
        public string Name { get { return _name.Value; } }
        public Reader Contents { get { return _contents.Value; } }

        private uint HeaderSize
        {
            get { return _elfSegmentReader.LayoutManager.GetLayout<ELFNoteHeader>().Size; }
        }

        private string ReadName()
        {
            ulong nameOffset = _noteHeaderOffset + HeaderSize;
            return _elfSegmentReader.WithRelativeAddressSpace(nameOffset, Align4(Header.NameSize)).Read<string>(0);
        }

        private Reader CreateContentsReader()
        {
            ulong contentsOffset = _noteHeaderOffset + HeaderSize + Align4(Header.NameSize);
            return _elfSegmentReader.WithRelativeAddressSpace(contentsOffset, Align4(Header.ContentSize));
        }

        private static uint Align4(uint x)
        {
            return (x + 3U) & ~3U;
        }
    }
}
