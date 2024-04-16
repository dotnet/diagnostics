// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Microsoft.FileFormats.PE
{
    /// <summary>
    /// A very basic PE reader that can extract a few useful pieces of information
    /// </summary>
    public class PEFile : IDisposable
    {
        // PE file
        private readonly bool _isDataSourceVirtualAddressSpace;
        private readonly Reader _headerReader;
        private readonly Lazy<ushort> _dosHeaderMagic;
        private readonly Lazy<ImageFileHeader> _fileHeader;
        private readonly Lazy<uint> _peHeaderOffset;
        private readonly Lazy<uint> _peSignature;
        private readonly Lazy<ImageOptionalHeaderMagic> _optionalHeaderMagic;
        private readonly Lazy<Reader> _fileReader;
        private readonly Lazy<ImageOptionalHeader> _optionalHeader;
        private readonly Lazy<List<ImageDataDirectory>> _imageDataDirectory;
        private readonly Lazy<IEnumerable<PEPdbRecord>> _pdb;
        private readonly Lazy<List<ImageSectionHeader>> _segments;
        private readonly Lazy<VsFixedFileInfo> _vsFixedFileInfo;
        private readonly Lazy<IEnumerable<PdbChecksum>> _pdbChecksum;
        private readonly Lazy<IEnumerable<PEPerfMapRecord>> _perfMapsV1;
        private readonly Lazy<Reader> _virtualAddressReader;
        private readonly Lazy<ImageExportDirectory> _exportDirectory;

        private const ushort ExpectedDosHeaderMagic = 0x5A4D;   // MZ
        private const int PESignatureOffsetLocation = 0x3C;
        private const uint ExpectedPESignature = 0x00004550;    // PE00
        private const int ImageDataDirectoryCount = 15;

        public const uint ChecksumLength = 4;
        public const uint CertDirectoryLength = 8;
        public const int CertDirectoryIndex = 4;

        public PEFile(IAddressSpace dataSource, bool isDataSourceVirtualAddressSpace = false)
        {
            _isDataSourceVirtualAddressSpace = isDataSourceVirtualAddressSpace;
            _headerReader = new Reader(dataSource);
            _dosHeaderMagic = new Lazy<ushort>(() => _headerReader.Read<ushort>(0));
            _peHeaderOffset = new Lazy<uint>(ReadPEHeaderOffset);
            _peSignature = new Lazy<uint>(() => _headerReader.Read<uint>(PEHeaderOffset));
            _fileHeader = new Lazy<ImageFileHeader>(ReadFileHeader);
            _optionalHeaderMagic = new Lazy<ImageOptionalHeaderMagic>(ReadOptionalHeaderMagic);
            _fileReader = new Lazy<Reader>(CreateFileReader);
            _optionalHeader = new Lazy<ImageOptionalHeader>(ReadOptionalHeader);
            _imageDataDirectory = new Lazy<List<ImageDataDirectory>>(ReadImageDataDirectory);
            _pdb = new Lazy<IEnumerable<PEPdbRecord>>(ReadPdbInfo);
            _segments = new Lazy<List<ImageSectionHeader>>(ReadSectionHeaders);
            _vsFixedFileInfo = new Lazy<VsFixedFileInfo>(ReadVersionResource);
            _pdbChecksum = new Lazy<IEnumerable<PdbChecksum>>(ReadPdbChecksum);
            _perfMapsV1 = new Lazy<IEnumerable<PEPerfMapRecord>>(ReadPerfMapV1Entries);
            _virtualAddressReader = new Lazy<Reader>(CreateVirtualAddressReader);
            _exportDirectory = new Lazy<ImageExportDirectory>(ReadExportDirectory);
        }

        public ushort DosHeaderMagic { get { return _dosHeaderMagic.Value; } }
        public uint PEHeaderOffset { get { return _peHeaderOffset.Value; } }
        public uint PESignature { get { return _peSignature.Value; } }
        public ImageFileHeader FileHeader { get { return _fileHeader.Value; } }
        public uint Timestamp { get { return FileHeader.TimeDateStamp; } }
        public ImageOptionalHeaderMagic OptionalHeaderMagic { get { return _optionalHeaderMagic.Value; } }
        public Reader FileReader { get { return _fileReader.Value; } }
        public ImageOptionalHeader OptionalHeader { get { return _optionalHeader.Value; } }
        public uint SizeOfImage { get { return OptionalHeader.SizeOfImage; } }
        public ReadOnlyCollection<ImageDataDirectory> ImageDataDirectory { get { return _imageDataDirectory.Value.AsReadOnly(); } }
        public IEnumerable<PEPdbRecord> Pdbs { get { return _pdb.Value; } }
        public Reader RelativeVirtualAddressReader { get { return _virtualAddressReader.Value; } }
        public ReadOnlyCollection<ImageSectionHeader> Segments { get { return _segments.Value.AsReadOnly(); } }
        public VsFixedFileInfo VersionInfo { get { return _vsFixedFileInfo.Value; } }
        public IEnumerable<PEPerfMapRecord> PerfMapsV1 { get { return _perfMapsV1.Value; } }
        public IEnumerable<PdbChecksum> PdbChecksums { get { return _pdbChecksum.Value; } }

        public void Dispose()
        {
            if (_headerReader.DataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public bool IsValid()
        {
            if (_headerReader.Length > sizeof(ushort))
            {
                try
                {
                    if (HasValidDosSignature.Check())
                    {
                        if (_headerReader.Length > PESignatureOffsetLocation)
                        {
                            return HasValidPESignature.Check();
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException)
                {
                }
            }
            return false;
        }

        public bool IsILImage { get { return ComDataDirectory.VirtualAddress != 0; } }

        /// <summary>
        /// The COM data directory.  In practice this is the metadata of an IL image.
        /// </summary>
        public ImageDataDirectory ComDataDirectory { get { return ImageDataDirectory[(int)ImageDirectoryEntry.ComDescriptor]; } }

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
                ImageExportDirectory exportDirectory = _exportDirectory.Value;
                if (exportDirectory is not null)
                {
                    for (int nameIndex = 0; nameIndex < exportDirectory.NumberOfNames; nameIndex++)
                    {
                        uint namePointerRVA = RelativeVirtualAddressReader.Read<uint>((ulong)(exportDirectory.AddressOfNames + (sizeof(uint) * nameIndex)));
                        if (namePointerRVA != 0)
                        {
                            string name = RelativeVirtualAddressReader.Read<string>(namePointerRVA);
                            if (name == symbolName)
                            {
                                ushort ordinalForNamedExport = RelativeVirtualAddressReader.Read<ushort>((ulong)(exportDirectory.AddressOfNameOrdinals + (sizeof(ushort) * nameIndex)));
                                offset = RelativeVirtualAddressReader.Read<uint>((ulong)(exportDirectory.AddressOfFunctions + (sizeof(uint) * ordinalForNamedExport)));
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException)
            {
            }
            offset = 0;
            return false;
        }

        private ImageExportDirectory ReadExportDirectory()
        {
            if (IsValid())
            {
                ImageDataDirectory exportTableDirectory = ImageDataDirectory[(int)ImageDirectoryEntry.Export];
                if (exportTableDirectory is not null)
                {
                    return RelativeVirtualAddressReader.Read<ImageExportDirectory>(exportTableDirectory.VirtualAddress);
                }
            }
            return null;
        }

        private uint ReadPEHeaderOffset()
        {
            HasValidDosSignature.CheckThrowing();
            return _headerReader.Read<uint>(PESignatureOffsetLocation);
        }

        private uint PEOptionalHeaderOffset
        {
            get { return _headerReader.SizeOf<ImageFileHeader>() + PEHeaderOffset + 0x4; }
        }

        public uint PEChecksumOffset
        {
            get { return PEOptionalHeaderOffset + 0x40; }
        }

        public uint CertificateTableOffset
        {
            get { return PEOptionalHeaderOffset + FileReader.SizeOf<ImageOptionalHeader>() + 0x20;  }
        }

        private ImageFileHeader ReadFileHeader()
        {
            HasValidPESignature.CheckThrowing();
            return _headerReader.Read<ImageFileHeader>(PEHeaderOffset + 0x4);
        }

        private ImageOptionalHeaderMagic ReadOptionalHeaderMagic()
        {
            ulong offset = PEOptionalHeaderOffset;
            return _headerReader.Read<ImageOptionalHeaderMagic>(offset);
        }

        private Reader CreateFileReader()
        {
            OptionalHeaderMagic.IsMagicValid.CheckThrowing();
            bool is64Bit = OptionalHeaderMagic.Magic == ImageMagic.Magic64;
            return new Reader(_headerReader.DataSource, new LayoutManager().AddPETypes(is64Bit));
        }

        private ImageOptionalHeader ReadOptionalHeader()
        {
            ulong offset = PEOptionalHeaderOffset;
            return FileReader.Read<ImageOptionalHeader>(offset);
        }

        private List<ImageDataDirectory> ReadImageDataDirectory()
        {
            ulong offset = PEOptionalHeaderOffset + FileReader.SizeOf<ImageOptionalHeader>();

            ImageDataDirectory[] result = _headerReader.ReadArray<ImageDataDirectory>(offset, ImageDataDirectoryCount);
            return new List<ImageDataDirectory>(result);
        }

        private List<ImageSectionHeader> ReadSectionHeaders()
        {
            ulong offset = PEOptionalHeaderOffset + FileHeader.SizeOfOptionalHeader;
            List<ImageSectionHeader> result = new(_headerReader.ReadArray<ImageSectionHeader>(offset, FileHeader.NumberOfSections));
            return result;
        }

        private IEnumerable<PEPdbRecord> ReadPdbInfo()
        {
            ImageDataDirectory imageDebugDirectory = ImageDataDirectory[(int)ImageDirectoryEntry.Debug];
            uint count = imageDebugDirectory.Size / FileReader.SizeOf<ImageDebugDirectory>();
            ImageDebugDirectory[] debugDirectories = RelativeVirtualAddressReader.ReadArray<ImageDebugDirectory>(imageDebugDirectory.VirtualAddress, count);

            foreach (ImageDebugDirectory directory in debugDirectories)
            {
                if (directory.Type == ImageDebugType.Codeview)
                {
                    ulong position = directory.AddressOfRawData;
                    CvInfoPdb70 pdb = RelativeVirtualAddressReader.Read<CvInfoPdb70>(ref position);
                    if (pdb.CvSignature == CvInfoPdb70.PDB70CvSignature)
                    {
                        bool isPortablePDB = directory.MinorVersion == ImageDebugDirectory.PortablePDBMinorVersion;
                        string fileName = RelativeVirtualAddressReader.Read<string>(position);
                        yield return new PEPdbRecord(isPortablePDB, fileName, new Guid(pdb.Signature), pdb.Age);
                    }
                }
            }
        }

        private IEnumerable<PdbChecksum> ReadPdbChecksum()
        {
            ImageDataDirectory imageDebugDirectory = ImageDataDirectory[(int)ImageDirectoryEntry.Debug];
            uint count = imageDebugDirectory.Size / FileReader.SizeOf<ImageDebugDirectory>();
            ImageDebugDirectory[] debugDirectories = RelativeVirtualAddressReader.ReadArray<ImageDebugDirectory>(imageDebugDirectory.VirtualAddress, count);

            foreach (ImageDebugDirectory directory in debugDirectories)
            {
                if (directory.Type == ImageDebugType.PdbChecksum)
                {
                    uint sizeOfData = directory.SizeOfData;
                    ulong position = directory.AddressOfRawData;
                    string algorithmName = RelativeVirtualAddressReader.Read<string>(position);
                    uint algorithmLength = (uint)algorithmName.Length;
                    uint length = sizeOfData - algorithmLength - 1; // -1 for null terminator
                    byte[] checksum = RelativeVirtualAddressReader.ReadArray<byte>(position + algorithmLength + 1 /* +1 for null terminator */, length);
                    yield return new PdbChecksum(algorithmName, checksum);
                }
            }
        }

        private IEnumerable<PEPerfMapRecord> ReadPerfMapV1Entries()
        {
            ImageDataDirectory imageDebugDirectory = ImageDataDirectory[(int)ImageDirectoryEntry.Debug];
            uint count = imageDebugDirectory.Size / FileReader.SizeOf<ImageDebugDirectory>();
            ImageDebugDirectory[] debugDirectories = RelativeVirtualAddressReader.ReadArray<ImageDebugDirectory>(imageDebugDirectory.VirtualAddress, count);

            foreach (ImageDebugDirectory directory in debugDirectories)
            {
                if (directory.Type == ImageDebugType.PerfMap && directory.MajorVersion == 1 && directory.MinorVersion == 0)
                {
                    ulong position = directory.AddressOfRawData;
                    PerfMapIdV1 perfmapEntryHeader = RelativeVirtualAddressReader.Read<PerfMapIdV1>(ref position);
                    if (perfmapEntryHeader.Magic == PerfMapIdV1.PerfMapEntryMagic)
                    {
                        string fileName = RelativeVirtualAddressReader.Read<string>(position);
                        yield return new PEPerfMapRecord(fileName, perfmapEntryHeader.Signature, perfmapEntryHeader.Version);
                    }
                }
            }
        }

        private const uint VersionResourceType = 16;
        private const uint VersionResourceName = 1;
        private const uint VersionResourceLanguage = 0x409;

        private VsFixedFileInfo ReadVersionResource()
        {
            ImageResourceDataEntry dataEntry = GetResourceDataEntry(VersionResourceType, VersionResourceName, VersionResourceLanguage);
            // If the version resource can't be found under the 0x409 language, try as language "neutral" (0)
            dataEntry ??= GetResourceDataEntry(VersionResourceType, VersionResourceName, 0);
            if (dataEntry != null)
            {
                VsVersionInfo info = RelativeVirtualAddressReader.Read<VsVersionInfo>(dataEntry.OffsetToData);
                if (info.Value.Signature == VsFixedFileInfo.FixedFileInfoSignature)
                {
                    return info.Value;
                }
            }
            return null;
        }

        private ImageResourceDataEntry GetResourceDataEntry(uint type, uint name, uint language)
        {
            uint resourceSectionRva = ImageDataDirectory[(int)ImageDirectoryEntry.Resource].VirtualAddress;
            ImageResourceDirectory resourceDirectory = RelativeVirtualAddressReader.Read<ImageResourceDirectory>(resourceSectionRva);

            if (GetNextLevelResourceEntryRva(resourceDirectory, type, resourceSectionRva, out uint nameTableRva))
            {
                if (GetNextLevelResourceEntryRva(resourceDirectory, name, resourceSectionRva + nameTableRva, out uint langTableRva))
                {
                    if (GetNextLevelResourceEntryRva(resourceDirectory, language, resourceSectionRva + langTableRva, out uint resourceDataEntryRva))
                    {
                        return RelativeVirtualAddressReader.Read<ImageResourceDataEntry>(resourceSectionRva + resourceDataEntryRva);
                    }
                }
            }
            return null;
        }

        private bool GetNextLevelResourceEntryRva(ImageResourceDirectory resourceDirectory, uint id, uint rva, out uint nextLevelRva)
        {
            ushort numNameEntries = resourceDirectory.NumberOfNamedEntries;
            ushort numIDEntries = resourceDirectory.NumberOfIdEntries;

            if (numNameEntries == ushort.MaxValue)
            {
                numNameEntries = 0;
            }

            if (numIDEntries == ushort.MaxValue)
            {
                numIDEntries = 0;
            }

            uint directorySize = RelativeVirtualAddressReader.SizeOf<ImageResourceDirectory>();
            uint entrySize = RelativeVirtualAddressReader.SizeOf<ImageResourceDirectoryEntry>();

            for (ushort i = numNameEntries; i < numNameEntries + numIDEntries; i++)
            {
                ImageResourceDirectoryEntry entry = RelativeVirtualAddressReader.Read<ImageResourceDirectoryEntry>(rva + directorySize + (i * entrySize));
                if (entry.Id == id)
                {
                    nextLevelRva = entry.OffsetToData & 0x7FFFFFFF;
                    return true;
                }
            }

            nextLevelRva = 0;
            return false;
        }

        private Reader CreateVirtualAddressReader()
        {
            if (_isDataSourceVirtualAddressSpace)
            {
                return _fileReader.Value;
            }
            else
            {
                return _fileReader.Value.WithAddressSpace(new PEAddressSpace(_headerReader.DataSource, 0, Segments));
            }
        }

        #region Validation Rules
        public ValidationRule HasValidDosSignature
        {
            get
            {
                return new ValidationRule("PE file does not have valid DOS header", () =>
                   DosHeaderMagic == ExpectedDosHeaderMagic);
            }
        }

        public ValidationRule HasValidPESignature
        {
            get
            {
                return new ValidationRule("PE file does not have a valid PE signature", () =>
                    PESignature == ExpectedPESignature);
            }
        }
        #endregion
    }

    public class PEAddressSpace : IAddressSpace
    {
        private Lazy<ulong> _length;
        private ReadOnlyCollection<ImageSectionHeader> _segments;
        private ulong _baseAddress;
        private IAddressSpace _addressSpace;

        public ulong Length
        {
            get
            {
                return _length.Value;
            }
        }

        public PEAddressSpace(IAddressSpace addressSpace, ulong baseAddress, ReadOnlyCollection<ImageSectionHeader> segments)
        {
            _length = new Lazy<ulong>(GetLength);
            _segments = segments;
            _baseAddress = baseAddress;
            _addressSpace = addressSpace;
        }

        public uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            ImageSectionHeader segment = _segments.Where(header => header.VirtualAddress <= position && position <= header.VirtualAddress + header.VirtualSize).FirstOrDefault();
            if (segment == null)
            {
                return 0;
            }
            ulong offset = _baseAddress + position - segment.VirtualAddress + segment.PointerToRawData;
            uint result = _addressSpace.Read(offset, buffer, bufferOffset, count);
            return result;
        }

        private ulong GetLength()
        {
            return _segments.Max(seg => seg.VirtualAddress + seg.VirtualSize);
        }
    }
}
