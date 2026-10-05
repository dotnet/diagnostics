// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.FileFormats;
using Microsoft.FileFormats.PE;
using Microsoft.SymbolStore.KeyGenerators;
using TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.SymbolStore.Tests
{
    public class PEFileKeyGenerationTests
    {
        readonly ITracer _tracer;

        public PEFileKeyGenerationTests(ITestOutputHelper output)
        {
            _tracer = new Tracer(output);
        }

        public class MockPEFile
        {
            private const uint DefaultTimestamp = 0x4D4F434B;
            private static readonly Version s_defaultVersion = new(1, 2, 3, 45);
            private readonly byte[] _image;

            public ImageFileMachine Machine { get; }
            public string FileName { get; }
            public string Id { get; }
            public bool IsRuntimeModule { get; }
            public bool IsSpecialFile { get; }
            public string[] DacDbiFiles { get; }
            public string[] SosFiles { get; }

            public MockPEFile(ImageFileMachine machine, string fileName, bool isRuntimeModule, bool isSpecialFile, string[] dacDbiFiles, string[] sosFiles)
            {
                Machine = machine;
                FileName = fileName;
                IsRuntimeModule = isRuntimeModule;
                IsSpecialFile = isSpecialFile;
                DacDbiFiles = dacDbiFiles;
                SosFiles = sosFiles;

                using MemoryStream stream = PEImageBuilder.Create(Machine, DefaultTimestamp, s_defaultVersion);
                _image = stream.ToArray();
                using PEReader reader = new(stream);
                Id = $"{reader.PEHeaders.CoffHeader.TimeDateStamp:X8}{reader.PEHeaders.PEHeader.SizeOfImage:x}";
            }

            public MemoryStream CreateStream()
            {
                return new MemoryStream(_image, writable: false);
            }
        }

        public static IEnumerable<object[]> MockPEFiles()
        {
            yield return new object[] { new MockPEFile(ImageFileMachine.Amd64, "clr.dll", true, false, new string[] { "mscordacwks.dll", "mscordacwks_amd64_amd64_1.2.3.45.dll", "mscordbi.dll" }, new string[] { "sos_amd64_amd64_1.2.3.45.dll" } ) };
            yield return new object[] { new MockPEFile(ImageFileMachine.Arm64, "clr.dll", true, false, new string[] { "mscordacwks.dll", "mscordacwks_arm64_arm64_1.2.3.45.dll", "mscordacwks_amd64_arm64_1.2.3.45.dll", "mscordbi.dll" }, new string[] { "sos_arm64_arm64_1.2.3.45.dll", "sos_amd64_arm64_1.2.3.45.dll" }) };
            yield return new object[] { new MockPEFile(ImageFileMachine.I386, "clr.dll", true, false, new string[] { "mscordacwks.dll", "mscordacwks_x86_x86_1.2.3.45.dll", "mscordbi.dll" }, new string[] { "sos_x86_x86_1.2.3.45.dll" }) };
            yield return new object[] { new MockPEFile(ImageFileMachine.Amd64, "coreclr.dll", true, false, new string[] { "mscordaccore.dll", "mscordaccore_amd64_amd64_1.2.3.45.dll", "mscordbi.dll" }, []) };
            yield return new object[] { new MockPEFile(ImageFileMachine.Arm64, "coreclr.dll", true, false, new string[] { "mscordaccore.dll", "mscordaccore_arm64_arm64_1.2.3.45.dll", "mscordaccore_amd64_arm64_1.2.3.45.dll", "mscordbi.dll" }, []) };
            yield return new object[] { new MockPEFile(ImageFileMachine.I386, "coreclr.dll", true, false, new string[] { "mscordaccore.dll", "mscordaccore_x86_x86_1.2.3.45.dll", "mscordbi.dll" }, []) };
            yield return new object[] { new MockPEFile(ImageFileMachine.Amd64, "mscordacwks.dll", false, true, [], []) };
            yield return new object[] { new MockPEFile(ImageFileMachine.Amd64, "mscordacwks_amd64_amd64_1.2.3.45.dll", false, true, [], []) };
            yield return new object[] { new MockPEFile(ImageFileMachine.Amd64, "mscordbi.dll", false, true, [], []) };
            yield return new object[] { new MockPEFile(ImageFileMachine.Amd64, "sos.dll", false, false, [], []) };
            yield return new object[] { new MockPEFile(ImageFileMachine.Amd64, "sos_amd64_amd64_1.2.3.45.dll", false, true, [], []) };
        }

        [Theory]
        [InlineData(ImageFileMachine.Amd64)]
        [InlineData(ImageFileMachine.Arm64)]
        [InlineData(ImageFileMachine.I386)]
        public void PEImageHasValidHeadersAndVersionResource(ImageFileMachine machine)
        {
            const uint Timestamp = 0x12345678;
            const FileInfoFlags Flags = FileInfoFlags.Debug | FileInfoFlags.SpecialBuild;
            using MemoryStream stream = PEImageBuilder.Create(machine, Timestamp, new Version(5, 6, 7, 8), Flags);
            using PEReader reader = new(stream, PEStreamOptions.LeaveOpen);
            PEHeader header = reader.PEHeaders.PEHeader;
            SectionHeader section = Assert.Single(reader.PEHeaders.SectionHeaders);

            Assert.Equal((ushort)machine, (ushort)reader.PEHeaders.CoffHeader.Machine);
            Assert.Equal(Timestamp, (uint)reader.PEHeaders.CoffHeader.TimeDateStamp);
            Assert.Equal(machine == ImageFileMachine.I386 ? PEMagic.PE32 : PEMagic.PE32Plus, header.Magic);
            Assert.Equal(16, header.NumberOfRvaAndSizes);
            Assert.Equal(0, header.SizeOfHeaders % header.FileAlignment);
            Assert.Equal(0, header.SizeOfImage % header.SectionAlignment);
            Assert.Equal(".rsrc", section.Name);
            Assert.Equal(header.SizeOfHeaders, section.PointerToRawData);
            Assert.Equal(0, section.SizeOfRawData % header.FileAlignment);
            Assert.Equal((long)section.PointerToRawData + section.SizeOfRawData, stream.Length);
            Assert.Equal(section.VirtualAddress, header.ResourceTableDirectory.RelativeVirtualAddress);
            Assert.Equal(section.VirtualSize, header.ResourceTableDirectory.Size);
            Assert.Equal(180, header.ResourceTableDirectory.Size);

            using BinaryReader resourceReader = new(stream, Encoding.Unicode, leaveOpen: true);
            stream.Position = section.PointerToRawData + 20;
            Assert.Equal(0x80000018U, resourceReader.ReadUInt32());
            stream.Position = section.PointerToRawData + 44;
            Assert.Equal(0x80000030U, resourceReader.ReadUInt32());
            stream.Position = section.PointerToRawData + 68;
            Assert.Equal(72U, resourceReader.ReadUInt32());
            Assert.Equal((uint)section.VirtualAddress + 88, resourceReader.ReadUInt32());
            Assert.Equal(92U, resourceReader.ReadUInt32());
            stream.Position = section.PointerToRawData + 88;
            Assert.Equal((ushort)92, resourceReader.ReadUInt16());
            Assert.Equal((ushort)52, resourceReader.ReadUInt16());
            Assert.Equal((ushort)0, resourceReader.ReadUInt16());
            Assert.Equal("VS_VERSION_INFO\0", new string(resourceReader.ReadChars(16)));
            Assert.Equal((ushort)0, resourceReader.ReadUInt16());
            Assert.Equal(VsFixedFileInfo.FixedFileInfoSignature, resourceReader.ReadUInt32());

            PEFile peFile = new(new StreamAddressSpace(stream));
            VsFixedFileInfo version = peFile.VersionInfo;
            Assert.Equal(5, version.FileVersionMajor);
            Assert.Equal(6, version.FileVersionMinor);
            Assert.Equal(7, version.FileVersionBuild);
            Assert.Equal(8, version.FileVersionRevision);
            Assert.Equal(Flags, version.FileFlags);
        }

        [Theory]
        [MemberData(nameof(MockPEFiles))]
        public void PEFileGenerateNoneKeys(MockPEFile mockPEFile)
        {
            using MemoryStream mockFileStream = mockPEFile.CreateStream();
            SymbolStoreFile mockSymbolStoreFile = new(mockFileStream, mockPEFile.FileName);
            PEFileKeyGenerator generator = new(_tracer, mockSymbolStoreFile);

            var noneKeys = generator.GetKeys(KeyTypeFlags.None);
            Assert.Empty(noneKeys);
        }

        [Theory]
        [MemberData(nameof(MockPEFiles))]
        public void PEFileGenerateIdentityKeys(MockPEFile mockPEFile)
        {
            using MemoryStream mockFileStream = mockPEFile.CreateStream();
            SymbolStoreFile mockSymbolStoreFile = new(mockFileStream, mockPEFile.FileName);
            PEFileKeyGenerator generator = new(_tracer, mockSymbolStoreFile);

            var identityKeys = generator.GetKeys(KeyTypeFlags.IdentityKey);
            Assert.True(identityKeys.Count() == 1);
            Assert.True(identityKeys.First().Index == $"{mockPEFile.FileName}/{mockPEFile.Id}/{mockPEFile.FileName}");
            Assert.True(identityKeys.First().IsClrSpecialFile == mockPEFile.IsSpecialFile);
        }

        [Theory]
        [MemberData(nameof(MockPEFiles))]
        public void PEFileGenerateClrKeys(MockPEFile mockPEFile)
        {
            using MemoryStream mockFileStream = mockPEFile.CreateStream();
            SymbolStoreFile mockSymbolStoreFile = new(mockFileStream, mockPEFile.FileName);
            PEFileKeyGenerator generator = new(_tracer, mockSymbolStoreFile);

            var clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys).ToDictionary((key) => key.Index);
            var specialFiles = mockPEFile.DacDbiFiles.Concat(mockPEFile.SosFiles);
            Assert.True(clrKeys.Count() == specialFiles.Count());
            foreach (var specialFileName in specialFiles)
            {
                Assert.True(clrKeys.ContainsKey($"{specialFileName}/{mockPEFile.Id}/{specialFileName}"));
            }
        }

        [Theory]
        [MemberData(nameof(MockPEFiles))]
        public void PEFileGenerateDacDbiKeys(MockPEFile mockPEFile)
        {
            using MemoryStream mockFileStream = mockPEFile.CreateStream();
            SymbolStoreFile mockSymbolStoreFile = new(mockFileStream, mockPEFile.FileName);
            PEFileKeyGenerator generator = new(_tracer, mockSymbolStoreFile);

            var dacdbiKeys = generator.GetKeys(KeyTypeFlags.DacDbiKeys).ToDictionary((key) => key.Index);
            Assert.True(dacdbiKeys.Count() == mockPEFile.DacDbiFiles.Count());
            foreach (var specialFileName in mockPEFile.DacDbiFiles)
            {
                Assert.True(dacdbiKeys.ContainsKey($"{specialFileName}/{mockPEFile.Id}/{specialFileName}"));
            }
        }

        [Theory]
        [MemberData(nameof(MockPEFiles))]
        public void PEFileGenerateRuntimeKeys(MockPEFile mockPEFile)
        {
            using MemoryStream mockFileStream = mockPEFile.CreateStream();
            SymbolStoreFile mockSymbolStoreFile = new(mockFileStream, mockPEFile.FileName);
            PEFileKeyGenerator generator = new(_tracer, mockSymbolStoreFile);

            var runtimeKeys = generator.GetKeys(KeyTypeFlags.RuntimeKeys);
            if (mockPEFile.IsRuntimeModule)
            {
                Assert.True(runtimeKeys.Count() == 1);
                Assert.True(runtimeKeys.First().Index == $"{mockPEFile.FileName}/{mockPEFile.Id}/{mockPEFile.FileName}");
            }
            else
            {
                Assert.Empty(runtimeKeys);
            }
        }
    }
}
