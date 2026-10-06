// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore.Tests
{
    internal static class PEImageBuilder
    {
        public static MemoryStream Create(ImageFileMachine machine, uint timestamp, Version version, FileInfoFlags fileFlags = 0)
        {
            PEHeaderBuilder header = new(
                machine: GetMachine(machine),
                sectionAlignment: 0x1000,
                fileAlignment: 0x200,
                imageCharacteristics: Characteristics.RelocsStripped | Characteristics.ExecutableImage | Characteristics.Dll);
            NativePEBuilder builder = new(header, timestamp, version, fileFlags);
            BlobBuilder image = new();
            builder.Serialize(image);
            return new MemoryStream(image.ToArray(), writable: false);
        }

        private static Machine GetMachine(ImageFileMachine machine)
        {
            return machine switch
            {
                ImageFileMachine.Amd64 => Machine.Amd64,
                ImageFileMachine.Arm64 => Machine.Arm64,
                ImageFileMachine.I386 => Machine.I386,
                _ => throw new ArgumentOutOfRangeException(nameof(machine))
            };
        }

        private sealed class NativePEBuilder : PEBuilder
        {
            private const int VersionOffset = 88;
            private const ushort VersionLength = 92;
            private const int ResourceLength = VersionOffset + VersionLength;

            private readonly Version _version;
            private readonly FileInfoFlags _fileFlags;
            private int _resourceRva;

            public NativePEBuilder(PEHeaderBuilder header, uint timestamp, Version version, FileInfoFlags fileFlags)
                : base(header, _ => new BlobContentId(Guid.Empty, timestamp))
            {
                _version = version;
                _fileFlags = fileFlags;
            }

            protected override ImmutableArray<Section> CreateSections()
            {
                return ImmutableArray.Create(
                    new Section(".rsrc", SectionCharacteristics.ContainsInitializedData | SectionCharacteristics.MemRead));
            }

            protected override BlobBuilder SerializeSection(string name, SectionLocation location)
            {
                if (name != ".rsrc")
                {
                    throw new InvalidOperationException($"Unexpected PE section '{name}'.");
                }

                _resourceRva = location.RelativeVirtualAddress;
                BlobBuilder section = new();
                WriteResourceDirectory(section, 16, 24, isSubdirectory: true); // RT_VERSION
                WriteResourceDirectory(section, 1, 48, isSubdirectory: true); // Resource name
                WriteResourceDirectory(section, 0x409, 72, isSubdirectory: false); // en-US
                section.WriteUInt32(checked((uint)(_resourceRva + VersionOffset)));
                section.WriteUInt32(VersionLength);
                section.WriteUInt32(0); // CodePage
                section.WriteUInt32(0); // Reserved

                section.WriteUInt16(VersionLength);
                section.WriteUInt16(52); // VS_FIXEDFILEINFO size
                section.WriteUInt16(0); // Binary data
                section.WriteUTF16("VS_VERSION_INFO\0");
                section.Align(4);

                section.WriteUInt32(VsFixedFileInfo.FixedFileInfoSignature);
                section.WriteUInt32(0); // Structure version is unused
                section.WriteUInt16(checked((ushort)_version.Minor));
                section.WriteUInt16(checked((ushort)_version.Major));
                section.WriteUInt16(checked((ushort)_version.Revision));
                section.WriteUInt16(checked((ushort)_version.Build));
                section.WriteUInt64(0); // Product version is unused
                section.WriteUInt32(0); // FileFlagsMask is unused
                section.WriteUInt32((uint)_fileFlags);
                section.WriteUInt32(0); // FileOS is unused
                section.WriteUInt32(0); // FileType is unused
                section.WriteUInt32(0); // FileSubtype is unused
                section.WriteUInt64(0); // FileDate is unused

                if (section.Count != ResourceLength)
                {
                    throw new InvalidOperationException($"Expected a {ResourceLength}-byte resource section, but wrote {section.Count} bytes.");
                }

                return section;
            }

            protected override PEDirectoriesBuilder GetDirectories()
            {
                return new PEDirectoriesBuilder
                {
                    ResourceTable = new DirectoryEntry(_resourceRva, ResourceLength)
                };
            }

            private static void WriteResourceDirectory(BlobBuilder builder, uint id, uint offset, bool isSubdirectory)
            {
                builder.WriteUInt32(0); // Characteristics
                builder.WriteUInt32(0); // TimeDateStamp
                builder.WriteUInt32(0); // MajorVersion and MinorVersion
                builder.WriteUInt16(0); // NumberOfNamedEntries
                builder.WriteUInt16(1); // NumberOfIdEntries
                builder.WriteUInt32(id);
                builder.WriteUInt32(isSubdirectory ? offset | 0x80000000 : offset);
            }
        }
    }
}
