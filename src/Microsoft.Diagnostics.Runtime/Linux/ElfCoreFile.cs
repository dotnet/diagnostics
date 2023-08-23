// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// A helper class to read linux coredumps.
    /// </summary>
    internal sealed class ElfCoreFile : IDisposable
    {
        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        private readonly Reader _reader;
        private ImmutableDictionary<ulong, ElfLoadedImage>? _loadedImages;
        private readonly Dictionary<ulong, ulong> _auxvEntries = new();
        private ElfVirtualAddressSpace? _virtualAddressSpace;

        /// <summary>
        /// All coredumps are themselves ELF files.  This property returns the ElfFile that represents this coredump.
        /// </summary>
        public ElfFile ElfFile { get; }

        /// <summary>
        /// Enumerates all prstatus notes contained within this coredump.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<IElfPRStatus> EnumeratePRStatus()
        {
            ElfMachine architecture = ElfFile.Header.Architecture;

            return GetNotes(ElfNoteType.PrpsStatus).Select<ElfNote, IElfPRStatus>(r => {
                return architecture switch
                {
                    ElfMachine.EM_X86_64 => r.ReadContents<ElfPRStatusX64>(0),
                    ElfMachine.EM_ARM => r.ReadContents<ElfPRStatusArm>(0),
                    ElfMachine.EM_AARCH64 => r.ReadContents<ElfPRStatusArm64>(0),
                    ElfMachine.EM_386 => r.ReadContents<ElfPRStatusX86>(0),
                    _ => throw new NotSupportedException($"Invalid architecture {architecture}"),
                };
            });
        }

        /// <summary>
        /// Returns the Auxv value of the given type.
        /// </summary>
        public ulong GetAuxvValue(ElfAuxvType type)
        {
            LoadAuxvTable();
            _auxvEntries.TryGetValue((ulong)type, out ulong value);
            return value;
        }

        /// <summary>
        /// A mapping of all loaded images in the process.  The key is the base address that the module is loaded at.
        /// </summary>
        public ImmutableDictionary<ulong, ElfLoadedImage> LoadedImages => _loadedImages ??= LoadFileTable();

        /// <summary>
        /// Creates an ElfCoreFile from a file on disk.
        /// </summary>
        /// <param name="coredump">A full path to a coredump on disk.</param>
        /// <exception cref="InvalidDataException">Throws <see cref="InvalidDataException"/> if the file is not an Elf coredump.</exception>
        public ElfCoreFile(string coredump)
            : this(File.OpenRead(coredump))
        {
        }

        /// <summary>
        /// Creates an ElfCoreFile from a file on disk.
        /// </summary>
        /// <param name="stream">The Elf stream to read the coredump from.</param>
        /// <param name="leaveOpen">Whether to leave the given stream open after this class is disposed.</param>
        /// <exception cref="InvalidDataException">Throws <see cref="InvalidDataException"/> if the file is not an Elf coredump.</exception>
        public ElfCoreFile(Stream stream, bool leaveOpen = false)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;

            _reader = new Reader(new StreamAddressSpace(stream));
            ElfFile = new ElfFile(_reader);

            if (ElfFile.Header.Type != ElfHeaderType.Core)
                throw new InvalidDataException($"{stream.GetFilename() ?? "The given stream"} is not a coredump");

#if DEBUG
            _loadedImages = LoadFileTable();
#endif
        }

        /// <summary>
        /// Reads memory from the given coredump's virtual address space.
        /// </summary>
        /// <param name="address">An address in the target program's virtual address space.</param>
        /// <param name="buffer">The buffer to fill.</param>
        /// <returns>The number of bytes written into the buffer.</returns>
        public int ReadMemory(ulong address, Span<byte> buffer)
        {
            _virtualAddressSpace ??= new ElfVirtualAddressSpace(ElfFile.ProgramHeaders, _reader.DataSource);
            return _virtualAddressSpace.Read(address, buffer);
        }

        private IEnumerable<ElfNote> GetNotes(ElfNoteType type)
        {
            return ElfFile.Notes.Where(n => n.Type == type);
        }

        private void LoadAuxvTable()
        {
            if (_auxvEntries.Count != 0)
                return;

            ElfNote auxvNote = GetNotes(ElfNoteType.Aux).SingleOrDefault() ?? throw new BadImageFormatException($"No auxv entries in coredump");
            ulong position = 0;
            while (true)
            {
                ulong type;
                ulong value;
                if (ElfFile.Header.Is64Bit)
                {
                    ElfAuxv64 elfauxv64 = auxvNote.ReadContents<ElfAuxv64>(ref position);
                    type = elfauxv64.Type;
                    value = elfauxv64.Value;
                }
                else
                {
                    ElfAuxv32 elfauxv32 = auxvNote.ReadContents<ElfAuxv32>(ref position);
                    type = elfauxv32.Type;
                    value = elfauxv32.Value;
                }

                if (type == (ulong)ElfAuxvType.Null)
                {
                    break;
                }

                _auxvEntries.Add(type, value);
            }
        }

        private ImmutableDictionary<ulong, ElfLoadedImage> LoadFileTable()
        {
            ElfNote fileNote = GetNotes(ElfNoteType.File).Single();

            ulong position = 0;
            ulong entryCount;
            if (ElfFile.Header.Is64Bit)
            {
                ElfFileTableHeader64 header = fileNote.ReadContents<ElfFileTableHeader64>(ref position);
                entryCount = header.EntryCount;
            }
            else
            {
                ElfFileTableHeader32 header = fileNote.ReadContents<ElfFileTableHeader32>(ref position);
                entryCount = header.EntryCount;
            }

            ElfFileTableEntryPointers64[] fileTable = new ElfFileTableEntryPointers64[entryCount];
            Dictionary<string, ElfLoadedImage> lookup = new(fileTable.Length);

            for (int i = 0; i < fileTable.Length; i++)
            {
                if (ElfFile.Header.Is64Bit)
                {
                    fileTable[i] = fileNote.ReadContents<ElfFileTableEntryPointers64>(ref position);
                }
                else
                {
                    ElfFileTableEntryPointers32 entry = fileNote.ReadContents<ElfFileTableEntryPointers32>(ref position);
                    fileTable[i].Start = entry.Start;
                    fileTable[i].Stop = entry.Stop;
                    fileTable[i].PageOffset = entry.PageOffset;
                }
            }

            int size = (int)(fileNote.Header.ContentSize - position);
            byte[] bytes = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                int read = fileNote.ReadContents(position, bytes);
                int start = 0;
                for (int i = 0; i < fileTable.Length; i++)
                {
                    int end = start;
                    while (bytes[end] != 0)
                        end++;

                    string path = Encoding.UTF8.GetString(bytes, start, end - start);
                    start = end + 1;

                    if (!lookup.TryGetValue(path, out ElfLoadedImage? image))
                        image = lookup[path] = new ElfLoadedImage(ElfFile.VirtualAddressReader, ElfFile.Header.Is64Bit, path);

                    image.AddTableEntryPointers(fileTable[i]);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }

            ImmutableDictionary<ulong, ElfLoadedImage>.Builder result = ImmutableDictionary.CreateBuilder<ulong, ElfLoadedImage>();
            foreach (ElfLoadedImage image in lookup.Values)
            {
                result.Add(image.BaseAddress, image);
            }

            return result.ToImmutable();
        }

        public void Dispose()
        {
            if (!_leaveOpen)
                _stream.Dispose();
        }
    }
}
