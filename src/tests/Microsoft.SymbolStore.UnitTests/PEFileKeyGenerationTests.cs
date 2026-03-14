// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            public string Path { get; }
            public string FileName { get; }
            public string Id { get; }
            public bool IsRuntimeModule { get; }
            public bool IsSpecialFile { get; }
            public string[] DacDbiFiles { get; }
            public string[] SosFiles { get; }

            public MockPEFile(string path, string fileName, string guid, bool isRuntimeModule, bool isSpecialFile, string[] dacDbiFiles, string[] sosFiles)
            {
                Path = path;
                FileName = fileName;
                Id = guid;
                IsRuntimeModule = isRuntimeModule;
                IsSpecialFile = isSpecialFile;
                DacDbiFiles = dacDbiFiles;
                SosFiles = sosFiles;
            }
        }

        public static IEnumerable<object[]> MockPEFiles()
        {
            yield return new object[] { new MockPEFile("TestBinaries/mockclr_amd64.dll", "clr.dll", "4D4F434B434c52", true, false, new string[] { "mscordacwks.dll", "mscordacwks_amd64_amd64_1.2.3.45.dll", "mscordbi.dll" }, new string[] { "sos_amd64_amd64_1.2.3.45.dll" } ) };
            yield return new object[] { new MockPEFile("TestBinaries/mockclr_arm64.dll", "clr.dll", "4D4F434B434c52", true, false, new string[] { "mscordacwks.dll", "mscordacwks_arm64_arm64_1.2.3.45.dll", "mscordacwks_amd64_arm64_1.2.3.45.dll", "mscordbi.dll" }, new string[] { "sos_arm64_arm64_1.2.3.45.dll", "sos_amd64_arm64_1.2.3.45.dll" }) };
            yield return new object[] { new MockPEFile("TestBinaries/mockclr_i386.dll", "clr.dll", "4D4F434B434c52", true, false, new string[] { "mscordacwks.dll", "mscordacwks_x86_x86_1.2.3.45.dll", "mscordbi.dll" }, new string[] { "sos_x86_x86_1.2.3.45.dll" }) };
            yield return new object[] { new MockPEFile("TestBinaries/mockclr_amd64.dll", "coreclr.dll", "4D4F434B434c52", true, false, new string[] { "mscordaccore.dll", "mscordaccore_amd64_amd64_1.2.3.45.dll", "mscordbi.dll" }, []) };
            yield return new object[] { new MockPEFile("TestBinaries/mockclr_arm64.dll", "coreclr.dll", "4D4F434B434c52", true, false, new string[] { "mscordaccore.dll", "mscordaccore_arm64_arm64_1.2.3.45.dll", "mscordaccore_amd64_arm64_1.2.3.45.dll", "mscordbi.dll" }, []) };
            yield return new object[] { new MockPEFile("TestBinaries/mockclr_i386.dll", "coreclr.dll", "4D4F434B434c52", true, false, new string[] { "mscordaccore.dll", "mscordaccore_x86_x86_1.2.3.45.dll", "mscordbi.dll" }, []) };
            yield return new object[] { new MockPEFile("TestBinaries/mockdac.dll", "mscordacwks.dll", "4D4F434B444143", false, true, [], []) };
            yield return new object[] { new MockPEFile("TestBinaries/mockdac.dll", "mscordacwks_amd64_amd64_1.2.3.45.dll", "4D4F434B444143", false, true, [], []) };
            yield return new object[] { new MockPEFile("TestBinaries/mockdbi.dll", "mscordbi.dll", "4D4F434B444249", false, true, [], []) };
            yield return new object[] { new MockPEFile("TestBinaries/mocksos.dll", "sos.dll", "4D4F434B534f53", false, false, [], []) };
            yield return new object[] { new MockPEFile("TestBinaries/mocksos.dll", "sos_amd64_amd64_1.2.3.45.dll", "4D4F434B534f53", false, true, [], []) };
        }

        [Theory]
        [MemberData(nameof(MockPEFiles))]
        public void PEFileGenerateNoneKeys(MockPEFile mockPEFile)
        {
            using var mockFileStream = new FileStream(mockPEFile.Path, FileMode.Open, FileAccess.Read);
            var mockSymbolStoreFile = new SymbolStoreFile(mockFileStream, mockPEFile.FileName);
            var generator = new PEFileKeyGenerator(_tracer, mockSymbolStoreFile);

            var noneKeys = generator.GetKeys(KeyTypeFlags.None);
            Assert.Empty(noneKeys);
        }

        [Theory]
        [MemberData(nameof(MockPEFiles))]
        public void PEFileGenerateIdentityKeys(MockPEFile mockPEFile)
        {
            using var mockFileStream = new FileStream(mockPEFile.Path, FileMode.Open, FileAccess.Read);
            var mockSymbolStoreFile = new SymbolStoreFile(mockFileStream, mockPEFile.FileName);
            var generator = new PEFileKeyGenerator(_tracer, mockSymbolStoreFile);

            var identityKeys = generator.GetKeys(KeyTypeFlags.IdentityKey);
            Assert.True(identityKeys.Count() == 1);
            Assert.True(identityKeys.First().Index == $"{mockPEFile.FileName}/{mockPEFile.Id}/{mockPEFile.FileName}");
            Assert.True(identityKeys.First().IsClrSpecialFile == mockPEFile.IsSpecialFile);
        }

        [Theory]
        [MemberData(nameof(MockPEFiles))]
        public void PEFileGenerateClrKeys(MockPEFile mockPEFile)
        {
            using var mockFileStream = new FileStream(mockPEFile.Path, FileMode.Open, FileAccess.Read);
            var mockSymbolStoreFile = new SymbolStoreFile(mockFileStream, mockPEFile.FileName);
            var generator = new PEFileKeyGenerator(_tracer, mockSymbolStoreFile);

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
            using var mockFileStream = new FileStream(mockPEFile.Path, FileMode.Open, FileAccess.Read);
            var mockSymbolStoreFile = new SymbolStoreFile(mockFileStream, mockPEFile.FileName);
            var generator = new PEFileKeyGenerator(_tracer, mockSymbolStoreFile);

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
            using var mockFileStream = new FileStream(mockPEFile.Path, FileMode.Open, FileAccess.Read);
            var mockSymbolStoreFile = new SymbolStoreFile(mockFileStream, mockPEFile.FileName);
            var generator = new PEFileKeyGenerator(_tracer, mockSymbolStoreFile);

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
