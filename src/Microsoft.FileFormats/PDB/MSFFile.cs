// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.FileFormats.PDB
{
    /// <summary>
    /// This class can read from PDB files that use the MSF container format.
    /// </summary>
    internal sealed class MSFFile : IMSFFile
    {
        private Reader[] _streams;

        private MSFFile(Reader[] streams)
        {
            _streams = streams;
        }

        internal static MSFFile OpenInternal(Reader fileReader, PDBFileHeader msfFileHeader)
        {
            // Read the Stream Directory and build the list of streams.

            uint pageSize = msfFileHeader.PageSize;

            uint secondLevelPageCount = ToPageCount(pageSize, msfFileHeader.DirectorySize);
            ulong pageIndicesOffset = fileReader.SizeOf<PDBFileHeader>();
            PDBPagedAddressSpace secondLevelPageList = CreatePagedAddressSpace(fileReader.DataSource, fileReader.DataSource, msfFileHeader.PageSize, pageIndicesOffset, secondLevelPageCount * sizeof(uint));
            PDBPagedAddressSpace directoryContent = CreatePagedAddressSpace(fileReader.DataSource, secondLevelPageList, msfFileHeader.PageSize, 0, msfFileHeader.DirectorySize);

            Reader directoryReader = new(directoryContent);
            ulong position = 0;
            uint countStreams = directoryReader.Read<uint>(ref position);
            uint[] streamSizes = directoryReader.ReadArray<uint>(ref position, countStreams);
            Reader[] streams = new Reader[countStreams];
            for (uint i = 0; i < streamSizes.Length; i++)
            {
                uint streamSize = streamSizes[i];
                streams[i] = new Reader(CreatePagedAddressSpace(fileReader.DataSource, directoryContent, pageSize, position, streamSize));
                position += ToPageCount(pageSize, streamSizes[i]) * sizeof(uint);
            }

            return new MSFFile(streams);
        }

        private static PDBPagedAddressSpace CreatePagedAddressSpace(IAddressSpace fileData, IAddressSpace indicesData, uint pageSize, ulong offset, uint length)
        {
            uint[] indices = new Reader(indicesData).ReadArray<uint>(offset, ToPageCount(pageSize, length));
            return new PDBPagedAddressSpace(fileData, indices, pageSize, length);
        }

        private static uint ToPageCount(uint pageSize, uint size)
        {
            return unchecked((pageSize + size - 1) / pageSize);
        }

        public uint NumStreams
        {
            get
            {
                return (uint)_streams.Length;
            }
        }

        public Reader GetStream(uint index)
        {
            return _streams[index];
        }

        public PDBContainerKind ContainerKind
        {
            get { return PDBContainerKind.MSF; }
        }

        public string ContainerKindSpecString
        {
            get { return "msf"; }
        }
    }
}
