// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.Implementation;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// A class to read information out of PE images (dll/exe).
    /// </summary>
    internal sealed unsafe class PEImage : IDisposable
    {
        private const ushort ExpectedDosHeaderMagic = 0x5A4D;   // MZ
        private const int PESignatureOffsetLocation = 0x3C;
        private const uint ExpectedPESignature = 0x00004550;    // PE00
        private const int ImageDataDirectoryCount = 15;
        private const int OptionalMagic32 = 0x010b;

        private int HeaderOffset => _peHeaderOffset + sizeof(uint);
        private int OptionalHeaderOffset => HeaderOffset + sizeof(ImageFileHeader);
        internal int SpecificHeaderOffset => OptionalHeaderOffset + sizeof(ImageOptionalHeader);
        private int DataDirectoryOffset => SpecificHeaderOffset + (IsPE64 ? 5 * 8 : 6 * 4);
        private int ImageDataDirectoryOffset => DataDirectoryOffset + ImageDataDirectoryCount * sizeof(ImageDataDirectory);

        private readonly ulong _imageBase;
        private readonly ulong _loadedImageBase;
        private readonly int[]? _relocations;

        private readonly Stream _stream;
        private int _offset;
        private readonly bool _leaveOpen;
        private readonly bool _isVirtual;

        private readonly int _peHeaderOffset;

        private ImmutableArray<PdbInfo> _pdbs;
        private ResourceEntry? _resources;

        private readonly ImageDataDirectory[] _directories = new ImageDataDirectory[ImageDataDirectoryCount];
        private readonly int _sectionCount;
        private ImageSectionHeader[]? _sections;
        private object? _metadata;
        private bool _disposed;

        public PEImage(FileStream stream, bool leaveOpen = false, ulong loadedImageBase = 0)
            : this(stream, leaveOpen, isVirtual: false, loadedImageBase)
        {
        }

        public PEImage(ReadVirtualStream stream, bool leaveOpen, bool isVirtual)
            : this(stream, leaveOpen, isVirtual, 0)
        {
        }

        public PEImage(ReaderStream stream, bool leaveOpen, bool isVirtual)
            : this(stream, leaveOpen, isVirtual, 0)
        {
        }

        /// <summary>
        /// Constructs a PEImage class for a given PE image (dll/exe) in memory.
        /// </summary>
        /// <param name="stream">A Stream that contains a PE image at its 0th offset.  This stream must be seekable.</param>
        /// <param name="leaveOpen">Whether or not to leave the stream open, if this is set to false stream will be
        /// disposed when this object is.</param>
        /// <param name="isVirtual">Whether stream points to a PE image mapped into an address space (such as in a live process or crash dump).</param>
        /// <param name="loadedImageBase">Provide a loaded image base so that the read API based on virtual addresses can be relocated</param>
        private PEImage(Stream stream, bool leaveOpen, bool isVirtual, ulong loadedImageBase)
        {
            _isVirtual = isVirtual;
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;

            if (!stream.CanSeek)
                throw new ArgumentException($"{nameof(stream)} is not seekable.");

            ushort dosHeaderMagic = Read<ushort>(0);
            if (dosHeaderMagic != ExpectedDosHeaderMagic)
            {
                IsValid = false;
                return;
            }

            _peHeaderOffset = Read<int>(PESignatureOffsetLocation);
            if (_peHeaderOffset == 0)
            {
                IsValid = false;
                return;
            }

            uint peSignature = Read<uint>(_peHeaderOffset);
            if (peSignature != ExpectedPESignature)
            {
                IsValid = false;
                return;
            }

            // Read image_file_header
            ImageFileHeader header = Read<ImageFileHeader>(HeaderOffset);
            _sectionCount = header.NumberOfSections;
            IndexTimeStamp = header.TimeDateStamp;

            if (TryRead(OptionalHeaderOffset, out ImageOptionalHeader optional))
            {
                IsPE64 = optional.Magic != OptionalMagic32;
                _imageBase = IsPE64 ? optional.ImageBase64 : optional.ImageBase;
                IndexFileSize = optional.SizeOfImage;

                // Read directories.  In order for us to read directories, we need to know if
                // this is a x64 image or not, hence why this is wrapped in the above TryRead
                SeekTo(DataDirectoryOffset);
                for (int i = 0; i < _directories.Length; i++)
                    if (!TryRead(out _directories[i]))
                        break;

            }

            if (loadedImageBase == 0)
                return;

            _loadedImageBase = loadedImageBase;


            int readingCursor = RvaToOffset(RelocationDataDirectory.VirtualAddress);
            int readingLimit = readingCursor + RelocationDataDirectory.Size;

            List<int> relocations = new();
            while (readingCursor < readingLimit)
            {
                ImageRelocation relocation = Read<ImageRelocation>(ref readingCursor);

                int numRelocationsInBlock = (relocation.blockSize - Unsafe.SizeOf<ImageRelocation>()) / Unsafe.SizeOf<ushort>();

                for (int i = 0; i < numRelocationsInBlock; i++)
                {
                    ushort reloc = Read<ushort>(ref readingCursor);
                    int withinPageOffset = (reloc & 0x0fff);
                    int type = reloc >> 12;
                    int rva = relocation.pageRVA + withinPageOffset;
                    int fileOffset = RvaToOffset(rva);

                    const int IMAGE_REL_BASED_ABSOLUTE = 0;
                    const int IMAGE_REL_BASED_HIGHLOW = 3;
                    const int IMAGE_REL_BASED_DIR64 = 10;

                    if (type == IMAGE_REL_BASED_ABSOLUTE)
                    {
                        continue;
                    }

                    relocations.Add(fileOffset);

                    if (type == IMAGE_REL_BASED_HIGHLOW)
                    {
                        relocations.Add(fileOffset + 3);
                    }
                    else if (type == IMAGE_REL_BASED_DIR64)
                    {
                        relocations.Add(fileOffset + 7);
                    }
                    else
                    {
                        // TODO: Implementation if this happens.
                    }
                }

                if (readingCursor % 4 != 0)
                {
                    readingCursor += 4 - (readingCursor % 4);
                }
            }
            _relocations = relocations.ToArray();
            Array.Sort(_relocations);
        }

        internal int ResourceVirtualAddress => ResourceDirectory.VirtualAddress;

        /// <summary>
        /// Gets the root resource node of this PEImage.
        /// </summary>
        public ResourceEntry Resources => _resources ??= CreateResourceRoot();

        /// <summary>
        /// Gets a value indicating whether the given Stream contains a valid DOS header and PE signature.
        /// </summary>
        public bool IsValid { get; } = true;

        /// <summary>
        /// Gets a value indicating whether this image is for a 64bit processor.
        /// </summary>
        public bool IsPE64 { get; }

        /// <summary>
        /// Gets a value indicating whether this image is managed. (.NET image)
        /// </summary>
        public bool IsManaged => ComDescriptorDirectory.VirtualAddress != 0;

        private ImageDataDirectory ExportDirectory => _directories[0];
        private ImageDataDirectory RelocationDataDirectory => _directories[5];
        private ImageDataDirectory DebugDataDirectory => _directories[6];
        private ImageDataDirectory ComDescriptorDirectory => _directories[14];
        internal ImageDataDirectory MetadataDirectory
        {
            get
            {
                // _metadata is an object to preserve atomicity
                if (_metadata is not null)
                    return (ImageDataDirectory)_metadata;

                ImageDataDirectory result = default;
                ImageDataDirectory corHdr = ComDescriptorDirectory;
                if (corHdr.VirtualAddress != 0 && corHdr.Size != 0)
                {
                    int offset = RvaToOffset(corHdr.VirtualAddress);
                    if (offset > 0)
                        result = Read<ImageCor20Header>(offset).MetaData;
                }

                _metadata = result;
                return result;
            }
        }
        private ImageDataDirectory ResourceDirectory => _directories[2];

        /// <summary>
        /// Gets the timestamp that this PE image is indexed under.
        /// </summary>
        public int IndexTimeStamp { get; }

        /// <summary>
        /// Gets the file size that this PE image is indexed under.
        /// </summary>
        public int IndexFileSize { get; }

        /// <summary>
        /// Gets a list of PDBs associated with this PE image.  PE images can contain multiple PDB entries,
        /// but by convention it's usually the last entry that is the most up to date.  Unless you need to enumerate
        /// all PDBs for some reason, you should use DefaultPdb instead.
        /// Undefined behavior if IsValid is <see langword="false"/>.
        /// </summary>
        public ImmutableArray<PdbInfo> Pdbs
        {
            get
            {
                if (!_pdbs.IsDefault)
                    return _pdbs;

                ImmutableArray<PdbInfo> pdbs = ReadPdbs();
                _pdbs = pdbs;
                return pdbs;
            }
        }

        /// <summary>
        /// Gets the PDB information for this module.  If this image does not contain PDB info (or that information
        /// wasn't included in Stream) this returns <see langword="null"/>.  If multiple PDB streams are present, this method returns the
        /// last entry.
        /// </summary>
        public PdbInfo? DefaultPdb => Pdbs.LastOrDefault();

        public void Dispose()
        {
            if (!_disposed)
            {
                if (!_leaveOpen)
                    _stream.Dispose();

                _disposed = true;
            }
        }

        /// <summary>
        /// Allows you to convert between a virtual address to a stream offset for this module.
        /// </summary>
        /// <param name="virtualAddress">The address to translate.</param>
        /// <returns>The position in the stream of the data, -1 if the virtual address doesn't map to any location of the PE image.</returns>
        public int RvaToOffset(int virtualAddress)
        {
            if (virtualAddress < 4096)
                return virtualAddress;

            if (_isVirtual)
                return virtualAddress;

            ImageSectionHeader[] sections = ReadSections();
            for (int i = 0; i < sections.Length; i++)
            {
                ref ImageSectionHeader section = ref sections[i];
                if (section.VirtualAddress <= virtualAddress && virtualAddress < section.VirtualAddress + section.VirtualSize)
                    return (int)(section.PointerToRawData + ((uint)virtualAddress - section.VirtualAddress));
            }

            return -1;
        }

        private int DoRead(ref int offset, Span<byte> dest)
        {
            int beginRead = offset;
            int endRead = offset + dest.Length - 1;
            int beginRelocation = -1;
            int endRelocation = -1;

            if (_relocations != null)
            {
                //
                // The _relocations array is an array of offsets that begin and end (both inclusively)
                // relocations.
                //
                // For example:
                // 32, 35, 36, 39 means [32, 36) and [36, 40) contains data that needs to be relocated.
                //
                // It is assumed that these never overlaps, so the array contain no duplicates, and it
                // is sorted, the array is prepared in the constructor by parsing the .reloc entries.
                //
                // The even/odd index always correspond to an open/close of the relocation interval.
                //
                // There is a possibility that the requested range partially contains a relocation, so
                // we need to make sure the reading range is extended in those cases. The code below
                // uses binary search to find the containing relocation records and deciding on
                // exactly how much we wanted to extend the read.
                //
                // The code also keeps track of which relocation to start and stop to apply so that
                // we can apply them after reading.
                //
                int beginSearch = Array.BinarySearch(_relocations, offset);
                if (beginSearch < 0)
                {
                    int beginSearchComplement = ~beginSearch;
                    if (beginSearchComplement == _relocations.Length)
                    {
                        // Case 1: ]
                        //           ^
                        // The read range starts after all relocations finishes, no need to extend the read
                        beginRelocation = _relocations.Length;
                    }
                    else if ((beginSearchComplement & 1) == 0)
                    {
                        // Case 2: ]   [
                        //           ^
                        // The read range starts between relocations, no need to extend the read
                        beginRelocation = beginSearchComplement;
                    }
                    else
                    {
                        // Case 3: [   ]
                        //           ^
                        // The read range starts within a relocation, extend the read
                        Debug.Assert((beginSearchComplement & 1) == 1);
                        Debug.Assert(beginSearch > 0);
                        beginRelocation = beginSearch - 1;
                        beginRead = _relocations[beginRelocation];
                    }
                }
                else
                {
                    if ((beginSearch & 1) == 0)
                    {
                        // Case 4: [
                        //         ^
                        // The read range starts exactly where a relocation start, no need to extend the read
                        beginRelocation = beginSearch;
                    }
                    else
                    {
                        // Case 5: ]
                        //         ^
                        // The read range starts exactly where a relocation end, extend the read
                        Debug.Assert(beginSearch > 0);
                        Debug.Assert((beginSearch & 1) == 1);
                        beginRelocation = beginSearch - 1;
                        beginRead = _relocations[beginRelocation];
                    }
                }
                int endSearch = Array.BinarySearch(_relocations, offset + dest.Length - 1);
                if (endSearch < 0)
                {
                    int endSearchComplement = ~endSearch;
                    if (endSearchComplement == _relocations.Length)
                    {
                        // Case 1: ]
                        //           ^
                        // The read range ends after all relocations finishes, no need to extend the read
                        endRelocation = _relocations.Length - 1;
                    }
                    else if ((endSearchComplement & 1) == 0)
                    {
                        // Case 2: ]   [
                        //           ^
                        // The read range ends between relocations, no need to extend the read
                        endRelocation = endSearchComplement - 1;
                    }
                    else
                    {
                        // Case 3: [   ]
                        //           ^
                        // The read range ends within a relocation, extend the read
                        Debug.Assert((endSearchComplement & 1) == 1);
                        Debug.Assert(endSearch > 0);
                        endRelocation = endSearch;
                        endRead = _relocations[endRelocation];
                    }
                }
                else
                {
                    if ((endSearch & 1) == 0)
                    {
                        // Case 4: [
                        //         ^
                        // The read range ends exactly where a relocation start, extend the read
                        endRelocation = endSearch + 1;
                        endRead = _relocations[endRelocation];
                    }
                    else
                    {
                        // Case 5: ]
                        //         ^
                        // The read range ends exactly where a relocation end, no need to extend the read
                        Debug.Assert(endSearch > 0);
                        Debug.Assert((endSearch & 1) == 1);
                        endRelocation = endSearch;
                    }
                }

                Debug.Assert((beginRelocation & 1) == 0);
                Debug.Assert((endRelocation & 1) == 1);
                Debug.Assert(beginRead <= offset);
                Debug.Assert(offset + dest.Length - 1 <= endRead);
            }

            int readSize = endRead - beginRead + 1;
            int headShift = offset - beginRead;

            byte[] rawBuffer = ArrayPool<byte>.Shared.Rent(readSize);
            Span<byte> buffer = new(rawBuffer, 0, readSize);

            SeekTo(beginRead);

            int read = _stream.Read(buffer);
            if (read < headShift)
            {
                // This is unexpected, we failed to read something that is supposed to be a reloc.
                SeekTo(_offset);
                offset = _offset;
                return 0;
            }

            read -= headShift;

            if (read > dest.Length)
            {
                read = dest.Length;
            }

            SeekTo(offset + read);
            offset += read;
            _offset = offset;

            if ((_relocations != null) && (beginRelocation < endRelocation))
            {
                for (int r = beginRelocation; r < endRelocation; r += 2)
                {
                    int relocationStartOffset = _relocations[r];
                    int relocationEndOffset = _relocations[r + 1];
                    int relocationSize = relocationEndOffset - relocationStartOffset + 1;

                    Debug.Assert(relocationStartOffset >= beginRead);
                    Debug.Assert(relocationEndOffset <= endRead);

                    byte[] beforeBytes = new byte[relocationSize];
                    for (int i = 0; i < relocationSize; i++)
                        beforeBytes[i] = buffer[relocationStartOffset - beginRead + i];

                    byte[]? afterBytes;
                    if (relocationSize == 4)
                    {
                        uint beforeValue = BitConverter.ToUInt32(beforeBytes, 0);
                        uint afterValue = beforeValue + (uint)(_loadedImageBase - _imageBase);
                        afterBytes = BitConverter.GetBytes(afterValue);
                    }
                    else
                    {
                        Debug.Assert(relocationSize == 8);
                        ulong beforeValue = BitConverter.ToUInt64(beforeBytes, 0);
                        ulong afterValue = beforeValue - _imageBase + _loadedImageBase;
                        afterBytes = BitConverter.GetBytes(afterValue);
                    }
                    Debug.Assert(afterBytes.Length == relocationSize);
                    for (int i = 0; i < relocationSize; i++)
                    {
                        buffer[relocationStartOffset - beginRead + i] = afterBytes[i];
                    }
                }
            }

            for (int i = 0; i < read; i++)
            {
                dest[i] = buffer[i + headShift];
            }

            ArrayPool<byte>.Shared.Return(rawBuffer);
            return read;
        }

        /// <summary>
        /// Reads data out of PE image into a native buffer.
        /// </summary>
        /// <param name="virtualAddress">The address to read from.</param>
        /// <param name="dest">The location to write the data.</param>
        /// <returns>The number of bytes actually read from the image and written to dest.</returns>
        public int Read(int virtualAddress, Span<byte> dest)
        {
            int offset = RvaToOffset(virtualAddress);
            if (offset == -1)
                return 0;

            return DoRead(ref offset, dest);
        }

        internal int ReadFromOffset(int offset, Span<byte> dest) => DoRead(ref offset, dest);

        /// <summary>
        /// Gets the File Version Information that is stored as a resource in the PE file.  (This is what the
        /// version tab a file's property page is populated with).
        /// </summary>
        public FileVersionInfo? GetFileVersionInfo()
        {
            IResourceNode? versionNode = Resources.GetChild("Version");
            if (versionNode is null || versionNode.Children.Length != 1)
                return null;

            versionNode = versionNode.Children[0];
            if (versionNode.Children.Length == 1)
                versionNode = versionNode.Children[0];

            int size = versionNode.Size;
            if (size < 16)  // Arbitrarily small value to ensure it's non-zero and has at least a little data in it
                return null;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                int count = versionNode.Read(buffer, 0);
                return new FileVersionInfo(buffer.AsSpan(0, count));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
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
                ImageDataDirectory exportTableDirectory = ExportDirectory;
                if (exportTableDirectory.VirtualAddress != 0 && exportTableDirectory.Size != 0)
                {
                    if (TryRead(RvaToOffset(exportTableDirectory.VirtualAddress), out ImageExportDirectory exportDirectory))
                    {
                        for (int nameIndex = 0; nameIndex < exportDirectory.NumberOfNames; nameIndex++)
                        {
                            int namePointerRVA = Read<int>(RvaToOffset(exportDirectory.AddressOfNames + (sizeof(uint) * nameIndex)));
                            if (namePointerRVA != 0)
                            {
                                string name = ReadNullTerminatedAscii(namePointerRVA, maxLength: 4096);
                                if (name == symbolName)
                                {
                                    ushort ordinalForNamedExport = Read<ushort>(RvaToOffset(exportDirectory.AddressOfNameOrdinals + (sizeof(ushort) * nameIndex)));
                                    int exportRVA = Read<int>(RvaToOffset(exportDirectory.AddressOfFunctions + (sizeof(uint) * ordinalForNamedExport)));
                                    offset = (uint)RvaToOffset(exportRVA);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }

            offset = 0;
            return false;
        }

        private string ReadNullTerminatedAscii(int rva, int maxLength)
        {
            StringBuilder builder = new(64);
            Span<byte> bytes = stackalloc byte[64];

            bool done = false;
            int read, totalRead = 0;
            while (!done && (read = Read(rva, bytes)) != 0)
            {
                rva += read;
                for (int i = 0; !done && i < read; i++, totalRead++)
                {
                    if (totalRead < maxLength)
                    {
                        if (bytes[i] != 0)
                            builder.Append((char)bytes[i]);
                        else
                            done = true;
                    }
                    else
                        done = true;
                }
            }

            return builder.ToString();
        }

        private ResourceEntry CreateResourceRoot()
        {
            return new ResourceEntry(this, null, "root", false, RvaToOffset(ResourceVirtualAddress));
        }

        internal T Read<T>(int offset) where T : unmanaged => Read<T>(ref offset);

        internal unsafe T Read<T>(ref int offset) where T : unmanaged
        {
            TryRead(ref offset, out T t);
            return t;
        }

        internal unsafe bool TryRead<T>(ref int offset, out T result) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            T t = default;

            int read = DoRead(ref offset, new Span<byte>(&t, size));

            if (read != size)
            {
                result = default;
                return false;
            }

            result = t;
            return true;
        }

        internal unsafe bool TryRead<T>(int offset, out T result) where T : unmanaged => TryRead(ref offset, out result);

        internal unsafe bool TryRead<T>(out T result) where T : unmanaged
        {
            int offset = _offset;
            return TryRead(ref offset, out result);
        }

        internal T Read<T>() where T : unmanaged => Read<T>(_offset);

        private void SeekTo(int offset)
        {
            if (offset != _offset)
            {
                _stream.Seek(offset, SeekOrigin.Begin);
                _offset = offset;
            }
        }

        private ImageSectionHeader[] ReadSections()
        {
            if (_sections is not null)
                return _sections;


            ImageSectionHeader[] sections = new ImageSectionHeader[_sectionCount];

            SeekTo(ImageDataDirectoryOffset);

            // Sanity check, there's a null row at the end of the data directory table
            if (!TryRead(out ulong zero) || zero != 0)
                return sections;

            for (int i = 0; i < sections.Length; i++)
            {
                if (!TryRead(out sections[i]))
                    break;
            }

            // We don't care about a race here
            _sections = sections;
            return sections;
        }

        private bool Read64Bit()
        {
            if (!IsValid)
                return false;

            int offset = OptionalHeaderOffset;
            if (!TryRead(ref offset, out ushort magic))
                return false;

            return magic != OptionalMagic32;
        }

        private ImmutableArray<PdbInfo> ReadPdbs()
        {
            try
            {
                ImageDataDirectory debugDirectory = DebugDataDirectory;
                if (debugDirectory.VirtualAddress != 0 && debugDirectory.Size != 0)
                {
                    int count = debugDirectory.Size / sizeof(ImageDebugDirectory);
                    int offset = RvaToOffset(debugDirectory.VirtualAddress);
                    if (offset == -1)
                        return ImmutableArray<PdbInfo>.Empty;

                    SeekTo(offset);
                    ImmutableArray<PdbInfo>.Builder result = ImmutableArray.CreateBuilder<PdbInfo>(count);
                    for (int i = 0; i < count; i++)
                    {
                        if (!TryRead(ref offset, out ImageDebugDirectory directory))
                            break;

                        if (directory.Type == ImageDebugType.CODEVIEW && directory.SizeOfData >= sizeof(CvInfoPdb70))
                        {
                            int ptr = _isVirtual ? directory.AddressOfRawData : directory.PointerToRawData;
                            if (TryRead(ptr, out int sig) && sig == CvInfoPdb70.PDB70CvSignature)
                            {
                                Guid guid = Read<Guid>();
                                int age = Read<int>();

                                // sizeof(sig) + sizeof(guid) + sizeof(age) - [null char] = 0x18 - 1
                                int nameLen = directory.SizeOfData - 0x18 - 1;
                                string? path = ReadString(nameLen);

                                if (path != null)
                                {
                                    PdbInfo pdb = new(path, guid, age);
                                    result.Add(pdb);
                                }
                            }

                        }
                    }

                    return result.MoveOrCopyToImmutable();
                }
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }

            return ImmutableArray<PdbInfo>.Empty;
        }

        private string? ReadString(int len)
        {
            if (len > 4096)
                len = 4096;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
            try
            {
                int offset = _offset;
                if (DoRead(ref offset, buffer) == 0)
                    return null;

                int index = Array.IndexOf(buffer, (byte)'\0', 0, len);
                if (index >= 0)
                    len = index;

                return Encoding.ASCII.GetString(buffer, 0, len);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}