// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore.Tests
{
    internal static class PEImageBuilder
    {
        // A data-only PE image with one resource section.
        public static MemoryStream Create(ImageFileMachine machine, uint timestamp, uint sizeOfImage, Version version, FileInfoFlags fileFlags = 0)
        {
            const int PEHeaderOffset = 0x80;
            const int OptionalHeaderOffset = PEHeaderOffset + 24;
            const int FileAlignment = 0x200;
            const int ResourceOffset = FileAlignment;
            const uint ResourceRva = 0x1000;
            const uint ResourceSectionCharacteristics = 0x40000040; // Initialized data | Read
            const uint VersionOffset = 88;
            const ushort VersionLength = 92;
            const uint ResourceLength = VersionOffset + VersionLength;

            bool is64Bit = machine == ImageFileMachine.Amd64 || machine == ImageFileMachine.Arm64;
            int optionalHeaderSize = is64Bit ? 240 : 224;
            int dataDirectoriesOffset = OptionalHeaderOffset + (is64Bit ? 112 : 96);
            int sectionHeaderOffset = OptionalHeaderOffset + optionalHeaderSize;
            MemoryStream stream = new(new byte[ResourceOffset + FileAlignment]);
            using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write((ushort)0x5A4D); // MZ
            stream.Position = 0x3C;
            writer.Write(PEHeaderOffset);
            stream.Position = PEHeaderOffset;
            writer.Write(0x00004550U); // PE signature
            writer.Write((ushort)machine);
            writer.Write((ushort)1); // NumberOfSections
            writer.Write(timestamp);
            stream.Position = OptionalHeaderOffset - 4;
            writer.Write((ushort)optionalHeaderSize);
            ushort characteristics = (ushort)(ImageFile.RelocsStripped | ImageFile.ExecutableImage | ImageFile.Dll);
            writer.Write(characteristics);

            writer.Write((ushort)(is64Bit ? ImageMagic.Magic64 : ImageMagic.Magic32));
            stream.Position = OptionalHeaderOffset + 8;
            writer.Write((uint)FileAlignment); // SizeOfInitializedData
            if (!is64Bit)
            {
                stream.Position = OptionalHeaderOffset + 24;
                writer.Write(ResourceRva); // BaseOfData
            }
            stream.Position = OptionalHeaderOffset + 32;
            writer.Write(ResourceRva); // SectionAlignment
            writer.Write((uint)FileAlignment);
            stream.Position = OptionalHeaderOffset + 56;
            writer.Write(sizeOfImage);
            writer.Write((uint)FileAlignment); // SizeOfHeaders
            stream.Position = OptionalHeaderOffset + (is64Bit ? 108 : 92);
            writer.Write(16U); // NumberOfRvaAndSizes
            stream.Position = dataDirectoriesOffset + 8 * (int)ImageDirectoryEntry.Resource;
            writer.Write(ResourceRva);
            writer.Write(ResourceLength);

            stream.Position = sectionHeaderOffset;
            writer.Write(Encoding.UTF8.GetBytes(".rsrc\0\0\0"));
            writer.Write(ResourceLength);
            writer.Write(ResourceRva);
            writer.Write((uint)FileAlignment); // SizeOfRawData
            writer.Write(ResourceOffset);
            stream.Position = sectionHeaderOffset + 36;
            writer.Write(ResourceSectionCharacteristics);

            // Directory offsets are relative to the resource section; leaf data uses an image RVA.
            stream.Position = ResourceOffset;
            WriteResourceDirectory(writer, 16, 24, true); // RT_VERSION
            WriteResourceDirectory(writer, 1, 48, true); // Resource name
            WriteResourceDirectory(writer, 0x409, 72, false); // en-US
            writer.Write(ResourceRva + VersionOffset);
            writer.Write((uint)VersionLength);
            writer.Write(0U); // CodePage
            writer.Write(0U); // Reserved

            stream.Position = ResourceOffset + VersionOffset;
            writer.Write(VersionLength);
            writer.Write((ushort)52); // VS_FIXEDFILEINFO size
            writer.Write((ushort)0); // Binary data
            writer.Write(Encoding.Unicode.GetBytes("VS_VERSION_INFO\0"));
            writer.Write((ushort)0); // Align VS_FIXEDFILEINFO to a 32-bit boundary

            writer.Write(VsFixedFileInfo.FixedFileInfoSignature);
            stream.Position += 4; // Structure version is unused
            writer.Write(checked((ushort)version.Minor));
            writer.Write(checked((ushort)version.Major));
            writer.Write(checked((ushort)version.Revision));
            writer.Write(checked((ushort)version.Build));
            stream.Position += 12; // Product version and FileFlagsMask are unused
            writer.Write((uint)fileFlags);

            stream.Position = 0;
            return stream;
        }

        private static void WriteResourceDirectory(BinaryWriter writer, uint id, uint offset, bool isSubdirectory)
        {
            writer.Write(0U); // Characteristics
            writer.Write(0U); // TimeDateStamp
            writer.Write(0U); // MajorVersion and MinorVersion
            writer.Write((ushort)0); // NumberOfNamedEntries
            writer.Write((ushort)1); // NumberOfIdEntries
            writer.Write(id);
            writer.Write(isSubdirectory ? offset | 0x80000000 : offset);
        }
    }
}
