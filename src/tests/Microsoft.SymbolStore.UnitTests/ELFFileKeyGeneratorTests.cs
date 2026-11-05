// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.SymbolStore.KeyGenerators;
using TestHelpers;
using Xunit;

namespace Microsoft.SymbolStore.Tests
{
    public class ELFFileKeyGeneratorTests
    {
        private const string BuildId = "0102030405060708090a0b0c0d0e0f1011121314";
        private readonly Tracer _tracer;

        public ELFFileKeyGeneratorTests(ITestOutputHelper output)
        {
            _tracer = new Tracer(output);
        }

        public static IEnumerable<object[]> ClassificationCases()
        {
            foreach (ELFHeaderType headerType in new[] { ELFHeaderType.Executable, ELFHeaderType.Shared, ELFHeaderType.Relocatable })
            {
                bool hasLoadableImage = headerType != ELFHeaderType.Relocatable;
                // A debug-only file retains allocated section sizes and flags, but not their contents.
                yield return new object[] { headerType, ELFSectionHeaderType.NoBits, 6UL, 16UL, true };
                yield return new object[] { headerType, ELFSectionHeaderType.ProgBits, 6UL, 16UL, !hasLoadableImage };
                // File-backed data, without any executable code, also identifies a module.
                yield return new object[] { headerType, ELFSectionHeaderType.ProgBits, 3UL, 16UL, !hasLoadableImage };
                yield return new object[] { headerType, ELFSectionHeaderType.ProgBits, 6UL, 0UL, true };
                yield return new object[] { headerType, ELFSectionHeaderType.ProgBits, 0UL, 16UL, true };
            }
        }

        [Theory]
        [MemberData(nameof(ClassificationCases))]
        public void ClassifiesModuleContents(ELFHeaderType headerType,
            ELFSectionHeaderType contentType, ulong contentFlags, ulong contentSize, bool debugCompanion)
        {
            using MemoryStream stream = CreateELF(headerType, ".debug_info", contentType, contentFlags, contentSize);
            // Neither a debug-looking extension nor the absence of .symtab determines the file's identity.
            using SymbolStoreFile file = new(stream, "module.dbg");
            ELFFileKeyGenerator generator = new(_tracer, file);
            string symbolIndex = $"_.debug/elf-buildid-sym-{BuildId}/_.debug";
            string identityIndex = debugCompanion ? symbolIndex : $"module.dbg/elf-buildid-{BuildId}/module.dbg";

            Assert.Equal(debugCompanion ? new[] { identityIndex } : new[] { identityIndex, symbolIndex },
                generator.GetKeys(KeyTypeFlags.IdentityKey).Select(key => key.Index));
            if (debugCompanion)
            {
                Assert.Empty(generator.GetKeys(KeyTypeFlags.SymbolKey));
            }
            else
            {
                Assert.Equal(symbolIndex, Assert.Single(generator.GetKeys(KeyTypeFlags.SymbolKey)).Index);
            }
            Assert.Equal(debugCompanion ? new[] { identityIndex } : new[] { identityIndex, symbolIndex },
                generator.GetKeys(KeyTypeFlags.IdentityKey | KeyTypeFlags.SymbolKey).Select(key => key.Index));
        }

        [Theory]
        [InlineData("module.dbg")]
        [InlineData("module.debug")]
        [InlineData("module.so")]
        [InlineData("module")]
        public void MalformedOnDiskLayoutDoesNotFallBackToFileExtension(string fileName)
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Executable, ".debug_info",
                ELFSectionHeaderType.ProgBits, 6, 16, sectionless: true);
            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                // Keep the build ID readable through PT_NOTE, but make section inspection fail.
                stream.Position = 40;
                writer.Write((ulong)stream.Length + 1);
                stream.Position = 58;
                writer.Write((ushort)64);
                writer.Write((ushort)1);
            }
            stream.Position = 0;
            using SymbolStoreFile file = new(stream, fileName);
            ELFFileKeyGenerator generator = new(_tracer, file);

            Assert.Empty(generator.GetKeys(KeyTypeFlags.IdentityKey));
            Assert.Empty(generator.GetKeys(KeyTypeFlags.IdentityKey | KeyTypeFlags.SymbolKey));
        }

        [Theory]
        [InlineData("module.dbg", true)]
        [InlineData("module.debug", true)]
        [InlineData("module.so", false)]
        [InlineData("module", false)]
        public void UnreadableMemoryBackedSectionsFallBackToFileExtension(string fileName, bool debugCompanion)
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Executable, ".debug_info",
                ELFSectionHeaderType.ProgBits, 6, 16, sectionless: true, virtualAddressSpace: true);
            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                stream.Position = 40;
                writer.Write((ulong)stream.Length + 1);
                stream.Position = 58;
                writer.Write((ushort)64);
                writer.Write((ushort)1);
            }
            stream.Position = 0;
            using ELFFile elfFile = new(new StreamAddressSpace(stream), isDataSourceVirtualAddressSpace: true);
            ELFFileKeyGenerator generator = new(_tracer, elfFile, fileName);
            string symbolIndex = $"_.debug/elf-buildid-sym-{BuildId}/_.debug";
            string identityIndex = debugCompanion ? symbolIndex : $"{fileName}/elf-buildid-{BuildId}/{fileName}";

            Assert.Equal(identityIndex, Assert.Single(generator.GetKeys(KeyTypeFlags.IdentityKey)).Index);
            Assert.Equal(debugCompanion ? new[] { identityIndex } : new[] { identityIndex, symbolIndex },
                generator.GetKeys(KeyTypeFlags.IdentityKey | KeyTypeFlags.SymbolKey).Select(key => key.Index));
        }

        [Theory]
        [InlineData(false, ".debug_info")]
        [InlineData(false, ".zdebug_info")]
        [InlineData(true, ".debug_info")]
        [InlineData(true, ".zdebug_info")]
        public void UnstrippedImageGeneratesTwoIdentityKeys(bool fileGenerator, string debugSection)
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Executable, debugSection,
                ELFSectionHeaderType.ProgBits, 6, 16);
            using SymbolStoreFile file = new(stream, "module");
            KeyGenerator generator = fileGenerator ? new FileKeyGenerator(_tracer, file) : new ELFFileKeyGenerator(_tracer, file);

            SymbolStoreKey[] keys = generator.GetKeys(KeyTypeFlags.IdentityKey).ToArray();
            Assert.Equal(new[]
            {
                $"module/elf-buildid-{BuildId}/module",
                $"_.debug/elf-buildid-sym-{BuildId}/_.debug"
            }, keys.Select(key => key.Index));
            Assert.All(keys, key => Assert.Equal(file.FileName, key.FullPathName));

            SymbolStoreKey symbolKey = Assert.Single(generator.GetKeys(KeyTypeFlags.SymbolKey));
            Assert.Equal(keys[1].Index, symbolKey.Index);
            Assert.Equal("module.dbg", symbolKey.FullPathName);
        }

        [Fact]
        public void StrippedImageGeneratesOneIdentityAndSeparateSymbolLookup()
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Executable, ".other",
                ELFSectionHeaderType.ProgBits, 6, 16);
            using SymbolStoreFile file = new(stream, "module");
            ELFFileKeyGenerator generator = new(_tracer, file);

            Assert.Equal($"module/elf-buildid-{BuildId}/module",
                Assert.Single(generator.GetKeys(KeyTypeFlags.IdentityKey)).Index);
            SymbolStoreKey symbolKey = Assert.Single(generator.GetKeys(KeyTypeFlags.SymbolKey));
            Assert.Equal($"_.debug/elf-buildid-sym-{BuildId}/_.debug", symbolKey.Index);
            Assert.Equal("module.dbg", symbolKey.FullPathName);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ManifestIncludesDiagnosticFileIdentities(bool unstripped)
        {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                using MemoryStream stream = CreateELF(ELFHeaderType.Shared, unstripped ? ".debug_info" : ".other",
                    ELFSectionHeaderType.ProgBits, 6, 16);
                File.WriteAllBytes(Path.Combine(directory, "libcoreclr.so"), stream.ToArray());
                File.WriteAllBytes(Path.Combine(directory, "libsos.so"), stream.ToArray());
                string manifestPath = Path.Combine(directory, "manifest.json");

                Assert.True(Microsoft.SymbolManifestGenerator.SymbolManifestGenerator.GenerateManifest(
                    _tracer, new DirectoryInfo(directory), manifestPath));
                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                JsonElement[] entries = manifest.RootElement.GetProperty("entries").EnumerateArray().ToArray();
                string[] expectedKeys = unstripped ? new[]
                {
                    $"libsos.so/elf-buildid-coreclr-{BuildId}/libsos.so",
                    $"libsos.so/elf-buildid-{BuildId}/libsos.so",
                    $"_.debug/elf-buildid-sym-{BuildId}/_.debug"
                } : new[]
                {
                    $"libsos.so/elf-buildid-coreclr-{BuildId}/libsos.so",
                    $"libsos.so/elf-buildid-{BuildId}/libsos.so"
                };
                Assert.Equal(expectedKeys, entries.Select(entry => entry.GetProperty("symbolKey").GetString()));
                Assert.All(entries, entry => Assert.Equal("libsos.so", entry.GetProperty("basedirRelativePath").GetString()));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void InvalidLoadAlignmentDoesNotClassifyImageAsLoadable()
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Executable, ".debug_info",
                ELFSectionHeaderType.ProgBits, 6, 16, loadAlignment: 3);
            using SymbolStoreFile file = new(stream, "module");
            ELFFileKeyGenerator generator = new(_tracer, file);

            Assert.Equal($"_.debug/elf-buildid-sym-{BuildId}/_.debug",
                Assert.Single(generator.GetKeys(KeyTypeFlags.IdentityKey)).Index);
        }

        [Theory]
        [InlineData(ELFHeaderType.LowOS, ".debug_info", true)]
        [InlineData(ELFHeaderType.LowOS, ".other", false)]
        [InlineData(ELFHeaderType.HighOS, ".debug_info", true)]
        [InlineData(ELFHeaderType.HighOS, ".other", false)]
        public void OSSpecificImageIsNotClassifiedAsLoadable(ELFHeaderType headerType, string debugSection, bool hasDebugInfo)
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Executable, debugSection,
                ELFSectionHeaderType.ProgBits, 6, 16);
            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                stream.Position = 16;
                writer.Write((ushort)headerType);
            }
            stream.Position = 0;
            using SymbolStoreFile file = new(stream, "module");
            ELFFileKeyGenerator generator = new(_tracer, file);

            List<string> expectedKeys = new();
            if (hasDebugInfo)
            {
                expectedKeys.Add($"_.debug/elf-buildid-sym-{BuildId}/_.debug");
            }
            Assert.Equal(expectedKeys, generator.GetKeys(KeyTypeFlags.IdentityKey).Select(key => key.Index));
            Assert.Contains(_tracer.VerboseMessages, message =>
                message.Contains("OS-specific ELF type") && message.Contains("unsupported"));
        }

        [Fact]
        public void OutOfFileLoadSegmentDoesNotClassifyImageAsLoadable()
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Executable, ".debug_info",
                ELFSectionHeaderType.ProgBits, 6, 16, loadOutsideFile: true);
            using SymbolStoreFile file = new(stream, "module");
            ELFFileKeyGenerator generator = new(_tracer, file);

            Assert.Equal($"_.debug/elf-buildid-sym-{BuildId}/_.debug",
                Assert.Single(generator.GetKeys(KeyTypeFlags.IdentityKey)).Index);
        }

        [Theory]
        [InlineData(ELFHeaderType.LowOS)]
        [InlineData(ELFHeaderType.HighOS)]
        public void UnreadableOSSpecificImageDoesNotFallBackToFileExtension(ELFHeaderType headerType)
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Executable, ".debug_info",
                ELFSectionHeaderType.ProgBits, 6, 16, sectionless: true);
            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                stream.Position = 16;
                writer.Write((ushort)headerType);
                stream.Position = 40;
                writer.Write((ulong)stream.Length + 1);
                stream.Position = 58;
                writer.Write((ushort)64);
                writer.Write((ushort)1);
            }
            stream.Position = 0;
            using SymbolStoreFile file = new(stream, "module.so");
            ELFFileKeyGenerator generator = new(_tracer, file);

            Assert.Empty(generator.GetKeys(KeyTypeFlags.IdentityKey));
            Assert.Contains(_tracer.VerboseMessages, message =>
                message.Contains("OS-specific ELF type") && message.Contains("unsupported"));
        }

        [Fact]
        public void SectionlessImageUsesValidLoadSegmentAsPayloadEvidence()
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Executable, ".debug_info",
                ELFSectionHeaderType.ProgBits, 6, 16, sectionless: true);
            using SymbolStoreFile file = new(stream, "module");
            ELFFileKeyGenerator generator = new(_tracer, file);

            Assert.Equal($"module/elf-buildid-{BuildId}/module",
                Assert.Single(generator.GetKeys(KeyTypeFlags.IdentityKey)).Index);
        }

        [Theory]
        [InlineData(KeyTypeFlags.RuntimeKeys, 1)]
        [InlineData(KeyTypeFlags.ClrKeys, 6)]
        [InlineData(KeyTypeFlags.DacDbiKeys, 4)]
        public void UnstrippedRuntimeProducesSpecialKeys(KeyTypeFlags flags, int count)
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Shared, ".debug_info",
                ELFSectionHeaderType.ProgBits, 6, 16);
            using SymbolStoreFile file = new(stream, "libcoreclr.so");
            ELFFileKeyGenerator generator = new(_tracer, file);
            string[] keys = generator.GetKeys(flags).Select(key => key.Index).ToArray();

            Assert.Equal(count, keys.Length);
            Assert.Equal(ELFFileKeyGenerator.GetKeys(flags, file.FileName,
                Enumerable.Range(1, 20).Select(value => (byte)value).ToArray(),
                symbolFile: false, symbolFileName: null)
                .Select(key => key.Index), keys);
        }

        [Fact]
        public void UnstrippedSharedDotnetProducesHostKey()
        {
            using MemoryStream stream = CreateELF(ELFHeaderType.Shared, ".debug_info",
                ELFSectionHeaderType.ProgBits, 6, 16);
            using SymbolStoreFile file = new(stream, "dotnet");
            ELFFileKeyGenerator generator = new(_tracer, file);

            Assert.Equal($"dotnet/elf-buildid-{BuildId}/dotnet",
                Assert.Single(generator.GetKeys(KeyTypeFlags.HostKeys)).Index);
        }

        public static IEnumerable<object[]> SymbolTableCases()
        {
            (string Kind, bool Useful)[] symbolKinds =
            {
                ("function", true),
                ("object", true),
                ("notype", true),
                ("tls", true),
                ("ifunc", true),
                ("absolute", true),
                ("dynamic", false),
                ("undefined", false),
                ("common", false),
                ("file", false),
                ("section", false),
                ("empty", false),
                ("zero-name", false),
                ("empty-name", false),
                ("bad-name", false),
                ("unterminated", false),
                ("bad-link", false),
                ("wrong-string-type", false),
                ("bad-entry-size", false),
                ("partial-entry", false),
                ("truncated", false),
                ("bad-string-range", false),
                ("bad-section-index", false)
            };

            foreach (bool is64Bit in new[] { false, true })
            {
                foreach (bool bigEndian in new[] { false, true })
                {
                    foreach (bool loadable in new[] { false, true })
                    {
                        foreach ((string kind, bool useful) in symbolKinds)
                        {
                            yield return new object[] { is64Bit, bigEndian, loadable, kind, useful };
                        }
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(SymbolTableCases))]
        public void ClassifiesSymbolTableWithoutDWARF(bool is64Bit, bool bigEndian, bool loadable, string kind, bool useful)
        {
            using MemoryStream stream = CreateSymbolTableELF(is64Bit, bigEndian, loadable, kind);
            using SymbolStoreFile file = new(stream, "module");
            FileKeyGenerator generator = new(_tracer, file);
            List<string> expected = new();
            if (loadable)
            {
                expected.Add($"module/elf-buildid-{BuildId}/module");
            }
            if (useful)
            {
                expected.Add($"_.debug/elf-buildid-sym-{BuildId}/_.debug");
            }
            SymbolStoreKey[] keys = generator.GetKeys(KeyTypeFlags.IdentityKey).ToArray();
            Assert.Equal(expected, keys.Select(key => key.Index));
            Assert.All(keys, key => Assert.Equal("module", key.FullPathName));
            Assert.Equal(loadable ? new[] { $"_.debug/elf-buildid-sym-{BuildId}/_.debug" } : Enumerable.Empty<string>(),
                generator.GetKeys(KeyTypeFlags.SymbolKey).Select(key => key.Index));
            if (loadable && !useful)
            {
                expected.Add($"_.debug/elf-buildid-sym-{BuildId}/_.debug");
            }
            Assert.Equal(expected, generator.GetKeys(KeyTypeFlags.IdentityKey | KeyTypeFlags.SymbolKey).Select(key => key.Index));
        }

        private static MemoryStream CreateSymbolTableELF(bool is64Bit, bool bigEndian, bool loadable, string kind)
        {
            int headerSize = is64Bit ? 64 : 52;
            int programSize = is64Bit ? 56 : 32;
            int sectionSize = is64Bit ? 64 : 40;
            int wordSize = is64Bit ? 8 : 4;
            int entrySize = is64Bit ? 24 : 16;
            int sectionOffset = headerSize + (loadable ? programSize : 0);
            int noteOffset = sectionOffset + 5 * sectionSize;
            int payloadOffset = noteOffset + 36;
            int symbolsOffset = payloadOffset + 16;
            int stringsOffset = symbolsOffset + entrySize * 2;
            MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
            {
                void Write(ulong value, int size)
                {
                    for (int i = 0; i < size; i++)
                    {
                        int shift = (bigEndian ? size - i - 1 : i) * 8;
                        writer.Write((byte)((value >> shift) & 0xff));
                    }
                }
                void Section(ELFSectionHeaderType type, ulong flags, ulong offset, ulong address, ulong size,
                    uint link = 0, ulong symbolEntrySize = 0)
                {
                    Write(0, 4);
                    Write((uint)type, 4);
                    Write(flags, wordSize);
                    Write(address, wordSize);
                    Write(offset, wordSize);
                    Write(size, wordSize);
                    Write(link, 4);
                    Write(0, 4);
                    Write(1, wordSize);
                    Write(symbolEntrySize, wordSize);
                }

                writer.Write(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F',
                    (byte)(is64Bit ? 2 : 1), (byte)(bigEndian ? 2 : 1), 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
                Write((ushort)(loadable ? ELFHeaderType.Shared : ELFHeaderType.Relocatable), 2);
                Write((uint)(is64Bit ? 62 : 3), 2);
                Write(1, 4);
                Write(0, wordSize);
                Write((ulong)(loadable ? headerSize : 0), wordSize);
                Write((ulong)sectionOffset, wordSize);
                Write(0, 4);
                Write((ulong)headerSize, 2);
                Write((ulong)(loadable ? programSize : 0), 2);
                Write(loadable ? 1UL : 0UL, 2);
                Write((ulong)sectionSize, 2);
                Write(5, 2);
                Write(0, 2);
                if (loadable)
                {
                    Write((uint)ELFProgramHeaderType.Load, 4);
                    if (is64Bit)
                    {
                        Write(5, 4);
                    }
                    Write((ulong)payloadOffset, wordSize);
                    Write((ulong)payloadOffset, wordSize);
                    Write(0, wordSize);
                    Write(16, wordSize);
                    Write(16, wordSize);
                    if (!is64Bit)
                    {
                        Write(5, 4);
                    }
                    Write(1, wordSize);
                }
                Section(ELFSectionHeaderType.Null, 0, 0, 0, 0);
                Section(ELFSectionHeaderType.Note, 0, (ulong)noteOffset, 0, 36);
                Section(loadable ? ELFSectionHeaderType.ProgBits : ELFSectionHeaderType.NoBits,
                    6, (ulong)payloadOffset, (ulong)payloadOffset, 16);
                ulong tableSize = (ulong)(kind == "empty" ? entrySize : entrySize * 2);
                Section(kind == "dynamic" ? ELFSectionHeaderType.DynSym : ELFSectionHeaderType.SymTab, 0,
                    kind == "truncated" ? (ulong)stringsOffset : (ulong)symbolsOffset, 0,
                    kind == "partial-entry" ? tableSize - 1 : tableSize,
                    kind == "bad-link" ? 9U : 4U, (ulong)(kind == "bad-entry-size" ? 1 : entrySize));
                Section(kind == "wrong-string-type" ? ELFSectionHeaderType.ProgBits : ELFSectionHeaderType.StrTab,
                    0, kind == "bad-string-range" ? ulong.MaxValue : (ulong)stringsOffset, 0, 6);
                Write(4, 4);
                Write(20, 4);
                Write(3, 4);
                writer.Write(new byte[] { (byte)'G', (byte)'N', (byte)'U', 0 });
                writer.Write(Enumerable.Range(1, 20).Select(value => (byte)value).ToArray());
                writer.Write(new byte[16 + entrySize]);
                byte symbolType = kind switch
                {
                    "notype" => 0,
                    "object" => 1,
                    "section" => 3,
                    "file" => 4,
                    "tls" => 6,
                    "ifunc" => 10,
                    _ => 2
                };
                ushort symbolSection = kind switch
                {
                    "undefined" => 0,
                    "common" => 0xfff2,
                    "absolute" => 0xfff1,
                    "bad-section-index" => 9,
                    _ => 2
                };
                Write(kind == "bad-name" ? 7UL : kind == "zero-name" ? 0UL : kind == "empty-name" ? 5UL : 1UL, 4);
                if (!is64Bit)
                {
                    Write((ulong)payloadOffset, 4);
                    Write(16, 4);
                }
                writer.Write(symbolType);
                writer.Write((byte)0);
                Write(symbolSection, 2);
                if (is64Bit)
                {
                    Write((ulong)payloadOffset, 8);
                    Write(16, 8);
                }
                writer.Write(Encoding.ASCII.GetBytes(kind == "unterminated" ? "\0funcx" : "\0func\0"));
            }
            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateELF(ELFHeaderType headerType, string debugSection,
            ELFSectionHeaderType contentType, ulong contentFlags, ulong contentSize, ulong loadAlignment = 1,
            bool sectionless = false, bool loadOutsideFile = false, bool virtualAddressSpace = false)
        {
            const int HeaderSize = 64;
            const int ProgramHeaderSize = 56;
            const int SectionHeaderSize = 64;
            const int SectionCount = 5;
            bool hasLoadSegment = headerType == ELFHeaderType.Executable || headerType == ELFHeaderType.Shared;
            int programHeaderOffset = hasLoadSegment ? HeaderSize : 0;
            int programHeaderCount = hasLoadSegment ? (sectionless ? 2 : 1) : 0;
            int sectionHeadersOffset = sectionless ? 0 : HeaderSize + ProgramHeaderSize * programHeaderCount;
            int sectionCount = sectionless ? 0 : SectionCount;
            int noteOffset = sectionless ? HeaderSize + ProgramHeaderSize * programHeaderCount :
                sectionHeadersOffset + SectionHeaderSize * SectionCount;
            int contentOffset = noteOffset + 36;
            int debugOffset = contentOffset + 16;
            int namesOffset = debugOffset + 1;
            string names = $"\0.note.gnu.build-id\0.custom\0{debugSection}\0.shstrtab\0";
            byte[] nameBytes = Encoding.ASCII.GetBytes(names);
            MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                // ELF64, little endian. The load segment maps only the allocated content section.
                writer.Write(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
                writer.Write((ushort)headerType);
                writer.Write((ushort)62);
                writer.Write(1U);
                writer.Write(0UL);
                writer.Write((ulong)programHeaderOffset);
                writer.Write((ulong)sectionHeadersOffset);
                writer.Write(0U);
                writer.Write((ushort)HeaderSize);
                writer.Write((ushort)(hasLoadSegment ? ProgramHeaderSize : 0));
                writer.Write((ushort)programHeaderCount);
                writer.Write((ushort)(sectionless ? 0 : SectionHeaderSize));
                writer.Write((ushort)sectionCount);
                writer.Write((ushort)(sectionless ? 0 : 4));

                if (hasLoadSegment)
                {
                    ulong loadFileOffset = (ulong)contentOffset + (loadOutsideFile ? 0x1000UL : 0);
                    writer.Write((uint)ELFProgramHeaderType.Load);
                    writer.Write(5U);
                    writer.Write(loadFileOffset);
                    writer.Write(loadFileOffset);
                    writer.Write(0UL);
                    writer.Write(contentSize);
                    writer.Write(contentSize);
                    writer.Write(loadAlignment);
                    if (sectionless)
                    {
                        writer.Write((uint)ELFProgramHeaderType.Note);
                        writer.Write(4U);
                        writer.Write((ulong)noteOffset);
                        writer.Write(virtualAddressSpace ? (ulong)noteOffset : 0UL);
                        writer.Write(0UL);
                        writer.Write(36UL);
                        writer.Write(36UL);
                        writer.Write(4UL);
                    }
                }

                if (!sectionless)
                {
                    writer.Write(new byte[SectionHeaderSize]);
                    WriteSection(writer, names.IndexOf(".note"), ELFSectionHeaderType.Note, 2, (ulong)noteOffset, 0, 36);
                    WriteSection(writer, names.IndexOf(".custom"), contentType, contentFlags, (ulong)contentOffset,
                        (contentFlags & 2) != 0 ? (ulong)contentOffset : 0, contentSize);
                    WriteSection(writer, names.IndexOf(debugSection), ELFSectionHeaderType.ProgBits, 0, (ulong)debugOffset, 0, 1);
                    WriteSection(writer, names.IndexOf(".shstrtab"), ELFSectionHeaderType.StrTab, 0,
                        (ulong)namesOffset, 0, (ulong)nameBytes.Length);
                }
                writer.Write(4U);
                writer.Write(20U);
                writer.Write(3U);
                writer.Write(new byte[] { (byte)'G', (byte)'N', (byte)'U', 0 });
                for (byte value = 1; value <= 20; value++)
                {
                    writer.Write(value);
                }
                writer.Write(new byte[17]);
                writer.Write(nameBytes);
            }
            stream.Position = 0;
            return stream;
        }

        private static void WriteSection(BinaryWriter writer, int name, ELFSectionHeaderType type, ulong flags,
            ulong offset, ulong virtualAddress, ulong size)
        {
            writer.Write((uint)name);
            writer.Write((uint)type);
            writer.Write(flags);
            writer.Write(virtualAddress);
            writer.Write(offset);
            writer.Write(size);
            writer.Write(0U);
            writer.Write(0U);
            writer.Write(1UL);
            writer.Write(0UL);
        }
    }
}
