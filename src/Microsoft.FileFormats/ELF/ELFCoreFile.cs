// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.FileFormats.ELF
{
    public class ELFCoreFile
    {
        private readonly ELFFile _elf;
        private readonly Lazy<ELFFileTable> _fileTable;
        private readonly Lazy<ELFLoadedImage[]> _images;

        public ELFCoreFile(IAddressSpace dataSource)
        {
            _elf = new ELFFile(dataSource);
            _fileTable = new Lazy<ELFFileTable>(ReadFileTable);
            _images = new Lazy<ELFLoadedImage[]>(ReadLoadedImages);
        }

        public ELFFileTable FileTable { get { return _fileTable.Value; } }
        public ELFLoadedImage[] LoadedImages { get { return _images.Value; } }
        public IAddressSpace DataSource { get { return _elf.VirtualAddressReader.DataSource; } }

        public bool IsValid()
        {
            return _elf.IsValid() && _elf.Header.Type == ELFHeaderType.Core;
        }

        public bool Is64Bit => _elf.Is64Bit;

        public IEnumerable<ELFProgramSegment> Segments => _elf.Segments;

        private ELFFileTable ReadFileTable()
        {
            foreach (ELFProgramSegment seg in _elf.Segments)
            {
                if (seg.Header.Type == ELFProgramHeaderType.Note)
                {
                    ELFNoteList noteList = new(seg.Contents);
                    foreach (ELFNote note in noteList.Notes)
                    {
                        if (note.Header.Type == ELFNoteType.File)
                        {
                            return new ELFFileTable(note.Contents);
                        }
                    }
                }
            }

            throw new BadInputFormatException("No ELF file table found");
        }

        private ELFLoadedImage[] ReadLoadedImages()
        {
            Dictionary<string, ELFLoadedImage> lookup = new();

            foreach (ELFFileTableEntry fte in FileTable.Files.Where(fte => !fte.Path.StartsWith("/dev/zero") && !fte.Path.StartsWith("/run/shm")))
            {
                string path = fte.Path;
                if (!lookup.TryGetValue(path, out ELFLoadedImage image))
                {
                    image = lookup[path] = new ELFLoadedImage(path);
                }
                image.AddTableEntryPointers(fte);
            }

            List<ELFLoadedImage> result = new();
            foreach (ELFLoadedImage image in lookup.Values)
            {
                image.Image = new ELFFile(_elf.VirtualAddressReader.DataSource, image.LoadAddress, isDataSourceVirtualAddressSpace: true);
                result.Add(image);
            }

            return result.ToArray();
        }
    }

    public class ELFLoadedImage
    {
        private ulong _loadAddress;
        private ulong _minimumPointer = ulong.MaxValue;

        public ELFLoadedImage(ELFFile image, ELFFileTableEntry entry)
        {
            Image = image;
            Path = entry.Path;
            _loadAddress = entry.LoadAddress;
        }

        public ELFLoadedImage(string path)
        {
            Path = path;
        }

        public ulong LoadAddress => _loadAddress == 0 ? _minimumPointer : _loadAddress;
        public string Path { get; }
        public ELFFile Image { get; internal set; }

        internal void AddTableEntryPointers(ELFFileTableEntry entry)
        {
            // There are cases (like .NET single-file modules) where the first NT_FILE entry isn't the ELF
            // or PE header (i.e the base address). The header is the first entry with PageOffset == 0. For
            // ELF modules there should only be one PageOffset == 0 entry but with the memory mapped PE
            // assemblies, there can be more than one PageOffset == 0 entry and the first one is the base
            // address.
            if (_loadAddress == 0 && entry.PageOffset == 0)
            {
                _loadAddress = entry.LoadAddress;
            }
            // If no load address was found, will use the lowest start address. There has to be at least one
            // entry. This fixes the .NET 5.0 MacOS ELF dumps which have modules with no PageOffset == 0 entries.
            _minimumPointer = Math.Min(entry.LoadAddress, _minimumPointer);
        }
    }

    public class ELFFileTableEntry
    {
        private readonly ELFFileTableEntryPointers _ptrs;

        public ELFFileTableEntry(string path, ELFFileTableEntryPointers ptrs)
        {
            Path = path;
            _ptrs = ptrs;
        }

        public ulong PageOffset => _ptrs.PageOffset;
        public ulong LoadAddress => _ptrs.Start;
        public string Path { get; private set; }
    }

    public class ELFFileTable
    {
        private readonly Reader _noteReader;
        private readonly Lazy<IEnumerable<ELFFileTableEntry>> _files;

        public ELFFileTable(Reader noteReader)
        {
            _noteReader = noteReader;
            _files = new Lazy<IEnumerable<ELFFileTableEntry>>(ReadFiles);
        }

        public IEnumerable<ELFFileTableEntry> Files { get { return _files.Value; } }

        private IEnumerable<ELFFileTableEntry> ReadFiles()
        {
            List<ELFFileTableEntry> files = new();
            ulong readPosition = 0;
            ELFFileTableHeader header = _noteReader.Read<ELFFileTableHeader>(ref readPosition);

            //TODO: sanity check the entryCount
            ELFFileTableEntryPointers[] ptrs = _noteReader.ReadArray<ELFFileTableEntryPointers>(ref readPosition, (uint)(ulong)header.EntryCount);
            for (int i = 0; i < (int)(ulong)header.EntryCount; i++)
            {
                string path = _noteReader.Read<string>(ref readPosition);

                // This substitution is for unloaded modules for which Linux appends " (deleted)" to the module name.
                path = path.Replace(" (deleted)", "");

                files.Add(new ELFFileTableEntry(path, ptrs[i]));
            }
            return files;
        }
    }
}
