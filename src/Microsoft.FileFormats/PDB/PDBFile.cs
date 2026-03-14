// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.FileFormats.PDB
{
    public class PDBFile : IDisposable
    {
        /// <summary>
        /// This provides access to the container file, which may be either MSF (uncompressed PDB)
        /// or MSFZ (compressed PDB). If this field is null, then this PDBFile is invalid.
        /// </summary>
        private readonly IMSFFile _msfFile;

        private readonly Lazy<Reader[]> _streams;
        private readonly Lazy<PDBNameStream> _nameStream;
        private readonly Lazy<DbiStream> _dbiStream;

        public PDBFile(IAddressSpace dataSource)
        {
            Reader reader = new(dataSource);
            _streams = new Lazy<Reader[]>(ReadDirectory);
            _nameStream = new Lazy<PDBNameStream>(() => {
                CheckValid();
                return new PDBNameStream(_msfFile.GetStream(1));
            });
            _dbiStream = new Lazy<DbiStream>(() => {
                CheckValid();
                return new DbiStream(_msfFile.GetStream(3));
            });

            if (reader.Length > reader.SizeOf<PDBFileHeader>())
            {
                PDBFileHeader msfFileHeader = reader.Read<PDBFileHeader>(0);
                if (msfFileHeader.IsMagicValid)
                {
                    MSFFile msfFile = MSFFile.OpenInternal(reader, msfFileHeader);
                    if (msfFile != null)
                    {
                        _msfFile = msfFile;
                        return;
                    }
                }
            }

            if (reader.Length > reader.SizeOf<MSFZFileHeader>())
            {
                MSFZFileHeader msfzFileHeader = reader.Read<MSFZFileHeader>(0);
                if (msfzFileHeader.IsMagicValid)
                {
                    MSFZFile msfzFile = MSFZFile.Open(dataSource);
                    if (msfzFile != null)
                    {
                        _msfFile = msfzFile;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Opens a PDB file. If the operation is not successful, this method will throw an exception, rather
        /// than return an invalid PDBFile object.
        /// </summary>
        public static PDBFile Open(IAddressSpace dataSource)
        {
            PDBFile pdbFile = new(dataSource);
            if (pdbFile.IsValid())
            {
                return pdbFile;
            }
            else
            {
                throw new BadInputFormatException("The specified file is not a PDB (uses neither MSF nor MSFZ container format).");
            }
        }

        [Obsolete("Switch to using NumStreams and GetStream. The 'Streams' collection is inefficient.")]
        public IList<Reader> Streams { get { return _streams.Value; } }

        private void CheckValid()
        {
            if (_msfFile == null)
            {
                throw new Exception("Object is not valid");
            }
        }

        /// <summary>
        /// The number of streams stored in the file. This will always be at least 1.
        /// </summary>
        public uint NumStreams
        {
            get
            {
                CheckValid();
                return _msfFile.NumStreams;
            }
        }

        /// <summary>
        /// Gets an object which can read the given stream.
        /// </summary>
        /// <param name="stream">The index of the stream. This must be less than NumStreams.</param>
        /// <returns>A Reader which can read the stream.</returns>
        public Reader GetStream(uint stream)
        {
            CheckValid();
            return _msfFile.GetStream(stream);
        }


        public PDBNameStream NameStream { get { return _nameStream.Value; } }
        public DbiStream DbiStream { get { return _dbiStream.Value; } }
        public uint Age { get { return NameStream.Header.Age; } }
        public uint DbiAge { get { return DbiStream.Header.Age; } }
        public Guid Signature { get { return new Guid(NameStream.Header.Guid); } }

        public void Dispose()
        {
            IDisposable msfFile = _msfFile as IDisposable;
            msfFile?.Dispose();
        }

        public bool IsValid()
        {
            return _msfFile != null;
        }

        private Reader[] ReadDirectory()
        {
            // We have already read the Stream Directory, in the constructor of PDBFile.
            // The purpose of this function is to provide backward compatibility with the old
            // 'Streams' property.  New code should not use `Streams`; instead, new code should
            // directly call `ReadStream`.

            int numStreams = (int)this.NumStreams;

            Reader[] streamReaders = new Reader[numStreams];

            for (int i = 1; i < numStreams; ++i)
            {
                streamReaders[i] = GetStream((uint)i);
            }
            return streamReaders;
        }

        /// <summary>
        /// Returns the container kind used for this PDB file.
        /// </summary>
        public PDBContainerKind ContainerKind
        {
            get
            {
                CheckValid();
                return _msfFile.ContainerKind;
            }
        }

        /// <summary>
        /// Returns a string which identifies the container kind, using a backward-compatible naming scheme.
        /// <summary>
        /// <para>
        /// The existing PDB format is identified as "pdb", while PDZ (MSFZ) is identified as "msfz0".
        /// This allows new versions of MSFZ to be identified and deployed without updating clients of this API.
        /// </para>
        public string ContainerKindSpecString
        {
            get
            {
                CheckValid();
                return _msfFile.ContainerKindSpecString;
            }
        }
    }

    /// <summary>
    /// Defines a virtual address paged address space that maps to an underlying physical
    /// paged address space with a different set of page Indices.
    /// </summary>
    /// <remarks>
    /// A paged address space is an address space where any address A can be converted
    /// to a page index and a page offset. A = index*page_size + offset.
    ///
    /// This paged address space maps each virtual address to a physical address by
    /// remapping each virtual page to potentially different physical page. If V is
    /// the virtual page index then pageIndices[V] is the physical page index.
    ///
    /// For example if pageSize is 0x100 and pageIndices is { 0x7, 0x9 } then
    /// virtual address 0x156 is:
    /// virtual page index 0x1, virtual offset 0x56
    /// physical page index 0x9, physical offset 0x56
    /// physical address is 0x956
    /// </remarks>
    internal sealed class PDBPagedAddressSpace : IAddressSpace
    {
        private readonly IAddressSpace _physicalAddresses;
        private readonly uint[] _pageIndices;
        private readonly uint _pageSize;

        public PDBPagedAddressSpace(IAddressSpace physicalAddresses, uint[] pageIndices, uint pageSize, ulong length)
        {
            _physicalAddresses = physicalAddresses;
            _pageIndices = pageIndices;
            _pageSize = pageSize;
            Length = length;
        }

        public ulong Length { get; private set; }

        public uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            if (position + count > Length)
            {
                throw new BadInputFormatException("Unexpected end of data: Expected " + count + " bytes.");
            }

            uint bytesRead = 0;
            while (bytesRead != count)
            {
                uint virtualPageOffset;
                ulong physicalPosition = GetPhysicalAddress(position, out virtualPageOffset);
                uint pageBytesToRead = Math.Min(_pageSize - virtualPageOffset, count - bytesRead);
                uint pageBytesRead = _physicalAddresses.Read(physicalPosition, buffer, bufferOffset + bytesRead, pageBytesToRead);
                bytesRead += pageBytesRead;
                position += pageBytesRead;
                if (pageBytesToRead != pageBytesRead)
                {
                    break;
                }
            }
            return bytesRead;
        }

        private ulong GetPhysicalAddress(ulong virtualAddress, out uint virtualOffset)
        {
            uint virtualPageIndex = (uint)(virtualAddress / _pageSize);
            virtualOffset = (uint)(virtualAddress - (virtualPageIndex * _pageSize));
            uint physicalPageIndex = _pageIndices[(int)virtualPageIndex];
            return (ulong)physicalPageIndex * _pageSize + virtualOffset;
        }
    }

    public class PDBNameStream
    {
        private readonly Reader _streamReader;
        private readonly Lazy<NameIndexStreamHeader> _header;


        public PDBNameStream(Reader streamReader)
        {
            _streamReader = streamReader;
            _header = new Lazy<NameIndexStreamHeader>(() => _streamReader.Read<NameIndexStreamHeader>(0));
        }

        public NameIndexStreamHeader Header { get { return _header.Value; } }
    }

    public class DbiStream
    {
        private readonly Reader _streamReader;
        private readonly Lazy<DbiStreamHeader> _header;


        public DbiStream(Reader streamReader)
        {
            _streamReader = streamReader;
            _header = new Lazy<DbiStreamHeader>(() => _streamReader.Read<DbiStreamHeader>(0));
        }

        public bool IsValid()
        {
            if (_streamReader.Length >= _streamReader.SizeOf<DbiStreamHeader>()) {
                return _header.Value.IsHeaderValid.Check();
            }
            return false;
        }

        public DbiStreamHeader Header { get { _header.Value.IsHeaderValid.CheckThrowing(); return _header.Value; } }
    }

    /// <summary>
    /// Specifies the kinds of PDB container formats.
    /// </summary>
    public enum PDBContainerKind
    {
        /// <summary>
        /// An uncompressed PDB.
        /// </summary>
        MSF,

        /// <summary>
        /// A compressed PDB, also known as a PDBZ or "PDB using MSFZ container".
        /// </summary>
        MSFZ,
    }
}
