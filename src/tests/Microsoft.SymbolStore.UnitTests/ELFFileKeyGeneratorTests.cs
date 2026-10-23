// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.FileFormats.ELF;
using Microsoft.SymbolStore.KeyGenerators;
using TestHelpers;
using Xunit;

namespace Microsoft.SymbolStore.Tests
{
    public class ELFFileKeyGeneratorTests
    {
        private const string BuildId = "0102030405060708090a0b0c0d0e0f1011121314";
        private readonly ITracer _tracer;

        public ELFFileKeyGeneratorTests(ITestOutputHelper output)
        {
            _tracer = new Tracer(output);
        }

        public static IEnumerable<object[]> ClassificationCases()
        {
            foreach (bool fileGenerator in new[] { false, true })
            {
                foreach (ELFHeaderType headerType in new[] { ELFHeaderType.Executable, ELFHeaderType.Shared, ELFHeaderType.Relocatable })
                {
                    foreach (string debugSection in new[] { ".debug_info", ".zdebug_info" })
                    {
                        // A debug-only file retains allocated section sizes and flags, but not their contents.
                        yield return new object[] { fileGenerator, headerType, debugSection, ELFSectionHeaderType.NoBits, 6UL, 16UL, true };
                        yield return new object[] { fileGenerator, headerType, debugSection, ELFSectionHeaderType.ProgBits, 6UL, 16UL, false };
                        // File-backed data, without any executable code, also identifies a module.
                        yield return new object[] { fileGenerator, headerType, debugSection, ELFSectionHeaderType.ProgBits, 3UL, 16UL, false };
                        yield return new object[] { fileGenerator, headerType, debugSection, ELFSectionHeaderType.ProgBits, 6UL, 0UL, true };
                        yield return new object[] { fileGenerator, headerType, debugSection, ELFSectionHeaderType.ProgBits, 0UL, 16UL, true };
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(ClassificationCases))]
        public void ClassifiesModuleContents(bool fileGenerator, ELFHeaderType headerType, string debugSection,
            ELFSectionHeaderType contentType, ulong contentFlags, ulong contentSize, bool symbolFile)
        {
            using MemoryStream stream = CreateELF(headerType, debugSection, contentType, contentFlags, contentSize);
            // Neither a debug-looking extension nor the absence of .symtab determines the file's identity.
            using SymbolStoreFile file = new(stream, "module.dbg");
            KeyGenerator generator = fileGenerator ? new FileKeyGenerator(_tracer, file) : new ELFFileKeyGenerator(_tracer, file);
            string symbolIndex = $"_.debug/elf-buildid-sym-{BuildId}/_.debug";
            string identityIndex = symbolFile ? symbolIndex : $"module.dbg/elf-buildid-{BuildId}/module.dbg";

            Assert.Equal(identityIndex, Assert.Single(generator.GetKeys(KeyTypeFlags.IdentityKey)).Index);
            if (symbolFile)
            {
                Assert.Empty(generator.GetKeys(KeyTypeFlags.SymbolKey));
            }
            else
            {
                Assert.Equal(symbolIndex, Assert.Single(generator.GetKeys(KeyTypeFlags.SymbolKey)).Index);
            }
            Assert.Equal(symbolFile ? new[] { identityIndex } : new[] { identityIndex, symbolIndex },
                generator.GetKeys(KeyTypeFlags.IdentityKey | KeyTypeFlags.SymbolKey).Select(key => key.Index));
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
                Enumerable.Range(1, 20).Select(value => (byte)value).ToArray(), symbolFile: false, symbolFileName: null)
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

        private static MemoryStream CreateELF(ELFHeaderType headerType, string debugSection,
            ELFSectionHeaderType contentType, ulong contentFlags, ulong contentSize)
        {
            const int HeaderSize = 64;
            const int SectionHeaderSize = 64;
            const int SectionCount = 5;
            const int NoteOffset = HeaderSize + SectionHeaderSize * SectionCount;
            const int ContentOffset = NoteOffset + 36;
            const int DebugOffset = ContentOffset + 16;
            const int NamesOffset = DebugOffset + 1;
            string names = $"\0.note.gnu.build-id\0.custom\0{debugSection}\0.shstrtab\0";
            byte[] nameBytes = Encoding.ASCII.GetBytes(names);
            MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                // ELF64, little endian. BuildID is read from the note section, without program headers.
                writer.Write(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
                writer.Write((ushort)headerType);
                writer.Write((ushort)62);
                writer.Write(1U);
                writer.Write(0UL);
                writer.Write(0UL);
                writer.Write((ulong)HeaderSize);
                writer.Write(0U);
                writer.Write((ushort)HeaderSize);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)SectionHeaderSize);
                writer.Write((ushort)SectionCount);
                writer.Write((ushort)4);

                writer.Write(new byte[SectionHeaderSize]);
                WriteSection(writer, names.IndexOf(".note"), ELFSectionHeaderType.Note, 2, NoteOffset, 36);
                WriteSection(writer, names.IndexOf(".custom"), contentType, contentFlags, ContentOffset, contentSize);
                WriteSection(writer, names.IndexOf(debugSection), ELFSectionHeaderType.ProgBits, 0, DebugOffset, 1);
                WriteSection(writer, names.IndexOf(".shstrtab"), ELFSectionHeaderType.StrTab, 0, NamesOffset, (ulong)nameBytes.Length);
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

        private static void WriteSection(BinaryWriter writer, int name, ELFSectionHeaderType type, ulong flags, ulong offset, ulong size)
        {
            writer.Write((uint)name);
            writer.Write((uint)type);
            writer.Write(flags);
            writer.Write(0UL);
            writer.Write(offset);
            writer.Write(size);
            writer.Write(0U);
            writer.Write(0U);
            writer.Write(1UL);
            writer.Write(0UL);
        }
    }
}
