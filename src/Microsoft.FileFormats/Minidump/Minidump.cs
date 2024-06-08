// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.FileFormats.Minidump
{
    /// <summary>
    /// A class which represents a Minidump (Microsoft "crash dump").
    /// </summary>
    public class Minidump
    {
        private readonly ulong _position;
        private readonly Reader _dataSourceReader;
        private readonly MinidumpHeader _header;
        private readonly MinidumpDirectory[] _directory;
        private readonly MinidumpSystemInfo _systemInfo;
        private readonly int _moduleListStream = -1;
        private readonly Lazy<List<MinidumpLoadedImage>> _loadedImages;
        private readonly Lazy<List<MinidumpSegment>> _memoryRanges;
        private readonly Lazy<Reader> _virtualAddressReader;

        /// <summary>
        /// Returns true if the given address space is a minidump.
        /// </summary>
        /// <param name="addressSpace">The address space to check.</param>
        /// <param name="position">The position of the minidump.</param>
        /// <returns>True if the address space is a minidump, false otherwise.</returns>
        public static bool IsValid(IAddressSpace addressSpace, ulong position = 0)
        {
            Reader headerReader = new(addressSpace);
            return headerReader.TryRead(position, out MinidumpHeader header) && header.IsSignatureValid.Check();
        }

        /// <summary>
        /// Constructor.  This constructor will throw exceptions if the file is not a minidump or contains corrupted data
        /// which interferes with parsing it.
        /// </summary>
        /// <param name="dataSource">The memory which backs this object.</param>
        /// <param name="position">The offset within addressSpace this minidump is located at.</param>
        public Minidump(IAddressSpace dataSource, ulong position = 0)
        {
            _position = position;

            Reader headerReader = new(dataSource);
            _header = headerReader.Read<MinidumpHeader>(_position);
            _header.IsSignatureValid.CheckThrowing();

            int systemIndex = -1;
            _directory = new MinidumpDirectory[_header.NumberOfStreams];
            ulong streamPos = _position + _header.StreamDirectoryRva;
            for (int i = 0; i < _directory.Length; i++)
            {
                _directory[i] = headerReader.Read<MinidumpDirectory>(ref streamPos);

                MinidumpStreamType streamType = _directory[i].StreamType;
                if (streamType == MinidumpStreamType.SystemInfoStream)
                {
                    Debug.Assert(systemIndex == -1);
                    systemIndex = i;
                }
                else if (streamType == MinidumpStreamType.ModuleListStream)
                {
                    Debug.Assert(_moduleListStream == -1);
                    _moduleListStream = i;
                }
            }

            if (systemIndex == -1)
            {
                throw new BadInputFormatException("Minidump does not contain a MINIDUMP_SYSTEM_INFO stream");
            }
            _systemInfo = headerReader.Read<MinidumpSystemInfo>(_position + _directory[systemIndex].Rva);

            _dataSourceReader = new Reader(dataSource, new LayoutManager().AddCrashDumpTypes(false, Is64Bit));
            _loadedImages = new Lazy<List<MinidumpLoadedImage>>(CreateLoadedImageList);
            _memoryRanges = new Lazy<List<MinidumpSegment>>(CreateSegmentList);
            _virtualAddressReader = new Lazy<Reader>(CreateVirtualAddressReader);
        }

        /// <summary>
        /// Returns the architecture of the target process.
        /// </summary>
        public ProcessorArchitecture Architecture { get { return _systemInfo.ProcessorArchitecture; } }

        /// <summary>
        /// A raw data reader for the underlying minidump file itself.
        /// </summary>
        public Reader DataSourceReader { get { return _dataSourceReader; } }

        /// <summary>
        /// A raw data reader for the memory in virtual address space of this minidump.
        /// </summary>
        public Reader VirtualAddressReader { get { return _virtualAddressReader.Value; } }

        /// <summary>
        /// A collection of loaded images in the minidump.  This does NOT contain unloaded modules.
        /// </summary>
        public ReadOnlyCollection<MinidumpLoadedImage> LoadedImages { get { return _loadedImages.Value.AsReadOnly(); } }

        /// <summary>
        /// A collection of all the memory segments in minidump.
        /// </summary>
        public ReadOnlyCollection<MinidumpSegment> Segments { get { return _memoryRanges.Value.AsReadOnly(); } }

        /// <summary>
        /// Returns true if the original process represented by this minidump was running as an x64 process or not.
        /// </summary>
        public bool Is64Bit
        {
            get
            {
                ProcessorArchitecture arch = _systemInfo.ProcessorArchitecture;
                return arch == ProcessorArchitecture.Alpha64 || arch == ProcessorArchitecture.Amd64 || arch == ProcessorArchitecture.Ia64;
            }
        }

        private Reader CreateVirtualAddressReader()
        {
            return _dataSourceReader.WithAddressSpace(new MinidumpVirtualAddressSpace(Segments, _dataSourceReader.DataSource));
        }

        private List<MinidumpLoadedImage> CreateLoadedImageList()
        {
            if (_moduleListStream == -1)
            {
                throw new BadInputFormatException("Minidump does not contain a ModuleStreamList in its directory.");
            }
            MinidumpModule[] modules = _dataSourceReader.ReadCountedArray<MinidumpModule>(_position + _directory[_moduleListStream].Rva);
            return new List<MinidumpLoadedImage>(modules.Select(module => new MinidumpLoadedImage(this, module)));
        }

        private List<MinidumpSegment> CreateSegmentList()
        {
            List<MinidumpSegment> ranges = new();

            foreach (MinidumpDirectory item in _directory)
            {
                if (item.StreamType == MinidumpStreamType.MemoryListStream)
                {
                    MinidumpMemoryDescriptor[] memoryRegions = _dataSourceReader.ReadCountedArray<MinidumpMemoryDescriptor>(_position + item.Rva);

                    foreach (MinidumpMemoryDescriptor region in memoryRegions)
                    {
                        MinidumpSegment range = new(region);
                        ranges.Add(range);
                    }

                }
                else if (item.StreamType == MinidumpStreamType.Memory64ListStream)
                {
                    ulong position = item.Rva;
                    ulong count = _dataSourceReader.Read<ulong>(ref position);
                    ulong rva = _dataSourceReader.Read<ulong>(ref position);

                    MinidumpMemoryDescriptor64[] memoryRegions = _dataSourceReader.ReadArray<MinidumpMemoryDescriptor64>(position + _position, checked((uint)count));
                    foreach (MinidumpMemoryDescriptor64 region in memoryRegions)
                    {
                        MinidumpSegment range = new(region, rva);
                        ranges.Add(range);

                        rva += region.DataSize;
                    }
                }
            }

            ranges.Sort((MinidumpSegment a, MinidumpSegment b) => a.VirtualAddress.CompareTo(b.VirtualAddress));
            return ranges;
        }
    }
}
