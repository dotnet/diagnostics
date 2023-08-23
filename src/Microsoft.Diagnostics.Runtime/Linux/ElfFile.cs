// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.IO;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// A helper class to read ELF files.
    /// </summary>
    internal sealed class ElfFile : IDisposable
    {
        private readonly ulong _position;
        private readonly bool _virtual;
        private readonly Stream? _stream;
        private readonly bool _leaveOpen;
        private Reader? _virtualAddressReader;
        private ImmutableArray<ElfNote> _notes;
        private ImmutableArray<ElfProgramHeader> _programHeaders;
        private ImmutableArray<byte> _buildId;
        private ElfDynamicSection? _dynamicSection;

        internal Reader Reader { get; }

        /// <summary>
        /// The ElfHeader of this file.
        /// </summary>
        public IElfHeader Header { get; }

        /// <summary>
        /// The list of ElfNotes for this file.
        /// </summary>
        public ImmutableArray<ElfNote> Notes
        {
            get
            {
                LoadNotes();
                return _notes;
            }
        }

        /// <summary>
        /// The list of ProgramHeaders for this file.
        /// </summary>
        public ImmutableArray<ElfProgramHeader> ProgramHeaders
        {
            get
            {
                LoadProgramHeaders();
                return _programHeaders;
            }
        }

        internal Reader VirtualAddressReader
        {
            get
            {
                CreateVirtualAddressReader();
                return _virtualAddressReader!;
            }
        }

        /// <summary>
        /// Returns the address of a module export symbol if found
        /// </summary>
        /// <param name="symbolName">symbol name (without the module name prepended)</param>
        /// <param name="offset">symbol offset returned</param>
        /// <returns>true if found</returns>
        public bool TryGetExportSymbol(string symbolName, out ulong offset)
        {
            try
            {
                if (DynamicSection is not null && DynamicSection.TryLookupSymbol(symbolName, out ElfSymbol? symbol) && symbol is not null)
                {
                    offset = (ulong)symbol.Value;
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
            }
            offset = 0;
            return false;
        }

        /// <summary>
        /// The ELFDynamicSection for this file, if it exists.
        /// </summary>
        internal ElfDynamicSection? DynamicSection
        {
            get
            {
                if (_dynamicSection is null)
                {
                    foreach (ElfProgramHeader? header in ProgramHeaders)
                    {
                        if (header.Type == ElfProgramHeaderType.Dynamic)
                        {
                            _dynamicSection = new ElfDynamicSection(Reader, Header.Is64Bit, _position + (_virtual ? header.VirtualAddress : header.FileOffset), _virtual ? header.VirtualSize : header.FileSize);
                            break;
                        }
                    }
                }

                return _dynamicSection;
            }
        }

        /// <summary>
        /// Returns the build id of this ELF module (or ImmutableArray.Default if it doesn't exist).
        /// </summary>
        public ImmutableArray<byte> BuildId
        {
            get
            {
                if (!_buildId.IsDefault)
                    return _buildId;

                if (Header.ProgramHeaderOffset != 0 && Header.ProgramHeaderEntrySize > 0 && Header.ProgramHeaderCount > 0)
                {
                    try
                    {
                        foreach (ElfNote note in Notes)
                        {
                            if (note.Type == ElfNoteType.PrpsInfo && note.Name.Equals("GNU"))
                            {
                                byte[] buildId = new byte[note.Header.ContentSize];
                                note.ReadContents(0, buildId);
                                return _buildId = buildId.AsImmutableArray();
                            }
                        }
                    }
                    catch (IOException)
                    {
                    }
                }

                return default;
            }
        }

        /// <summary>
        /// Creates an ElfFile from a file on disk.
        /// </summary>
        /// <param name="filename">A full path of an elf file on disk.</param>
        /// <exception cref="InvalidDataException">Throws <see cref="InvalidDataException"/> if the file is not an Elf coredump.</exception>
        public ElfFile(string filename)
            : this(File.OpenRead(filename))
        {
        }

        /// <summary>
        /// Creates an ElfFile from a file on disk.
        /// </summary>
        /// <param name="stream">The Elf stream to read the Elf file from.</param>
        /// <param name="leaveOpen">Whether to leave the given stream open after this class is disposed.</param>
        /// <exception cref="InvalidDataException">Throws <see cref="InvalidDataException"/> if the file is not an Elf file.</exception>
        public ElfFile(Stream stream, bool leaveOpen = false)
            : this(stream, position: 0, leaveOpen, isVirtual: false)
        {
        }

        /// <summary>
        /// Creates an ElfFile from a file on disk.
        /// </summary>
        /// <param name="stream">The Elf stream to read the Elf file from.</param>
        /// <param name="position">Base position of streawm</param>
        /// <param name="leaveOpen">Whether to leave the given stream open after this class is disposed.</param>
        /// <param name="isVirtual">Whether stream points to a ELF image mapped into an address space (such as in a live process or crash dump).</param>
        /// <exception cref="InvalidDataException">Throws <see cref="InvalidDataException"/> if the file is not an Elf file.</exception>
        public ElfFile(Stream stream, ulong position, bool leaveOpen, bool isVirtual)
            : this(new Reader(new RelativeAddressSpace(new StreamAddressSpace(stream), "ElfFile", startOffset: 0, (ulong)(stream ?? throw new ArgumentNullException(nameof(stream))).Length, (long)position)), position, isVirtual)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
        }

        public ElfFile(IDataReader reader, ulong position)
            : this(new Reader(new MemoryVirtualAddressSpace(reader)), position, isVirtual: true)
        {
        }

        internal ElfFile(Reader reader, ulong position = 0, bool isVirtual = false)
        {
            Reader = reader;
            _position = position;
            _virtual = isVirtual;

            if (isVirtual)
                _virtualAddressReader = reader;

            ElfHeaderCommon common;
            try
            {
                common = reader.Read<ElfHeaderCommon>(_position);
            }
            catch (IOException e)
            {
                throw new InvalidDataException($"{reader.DataSource.Name ?? "This coredump"} does not contain a valid ELF header.", e);
            }

            Header = common.GetHeader(reader, _position)!;
            if (Header is null)
                throw new InvalidDataException($"{reader.DataSource.Name ?? "This coredump"} does not contain a valid ELF header.");
        }

        internal ElfFile(IElfHeader header, Reader reader, ulong position = 0, bool isVirtual = false)
        {
            Reader = reader;
            _position = position;
            _virtual = isVirtual;

            if (isVirtual)
                _virtualAddressReader = reader;

            Header = header;
        }

        private void CreateVirtualAddressReader()
        {
            _virtualAddressReader ??= new Reader(new ElfVirtualAddressSpace(ProgramHeaders, Reader.DataSource));
        }

        private void LoadNotes()
        {
            if (!_notes.IsDefault)
                return;

            LoadProgramHeaders();

            ImmutableArray<ElfNote>.Builder notes = ImmutableArray.CreateBuilder<ElfNote>();
            foreach (ElfProgramHeader programHeader in _programHeaders)
            {
                if (programHeader.Type == ElfProgramHeaderType.Note)
                {
                    Reader reader = new(programHeader.AddressSpace);
                    ulong position = 0;
                    while (position < reader.DataSource.Length)
                    {
                        ElfNote note = new(reader, position);
                        notes.Add(note);

                        position += note.TotalSize;
                    }
                }
            }

            _notes = notes.MoveOrCopyToImmutable();
        }

        private void LoadProgramHeaders()
        {
            if (!_programHeaders.IsDefault)
                return;

            // Calculate the loadBias. It is usually just the base address except for some executable modules.
            long loadBias = (long)_position;
            if (loadBias > 0)
            {
                for (uint i = 0; i < Header.ProgramHeaderCount; i++)
                {
                    ElfProgramHeader header = new(Reader, Header.Is64Bit, _position + Header.ProgramHeaderOffset + i * Header.ProgramHeaderEntrySize, 0, _virtual);
                    if (header.Type == ElfProgramHeaderType.Load && header.FileOffset == 0)
                    {
                        loadBias -= (long)header.VirtualAddress;
                    }
                }
            }

            // Build the program segments using the load bias
            ImmutableArray<ElfProgramHeader>.Builder programHeaders = ImmutableArray.CreateBuilder<ElfProgramHeader>(Header.ProgramHeaderCount);
            programHeaders.Count = programHeaders.Capacity;

            for (uint i = 0; i < programHeaders.Count; i++)
                programHeaders[(int)i] = new ElfProgramHeader(Reader, Header.Is64Bit, _position + Header.ProgramHeaderOffset + i * Header.ProgramHeaderEntrySize, loadBias, _virtual);

            _programHeaders = programHeaders.MoveOrCopyToImmutable();
        }

        public void Dispose()
        {
            if (!_leaveOpen)
                _stream?.Dispose();
        }
    }
}