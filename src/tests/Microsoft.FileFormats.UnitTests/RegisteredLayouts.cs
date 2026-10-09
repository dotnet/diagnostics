// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.Minidump;
using Microsoft.FileFormats.PDB;
using Microsoft.FileFormats.PE;
using Xunit;

namespace Microsoft.FileFormats.Tests
{
    public class RegisteredLayouts
    {
        [Fact]
        public void ReadRegisteredLayouts() => RegisteredScenarios.Run();

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public void CustomLayoutsMatchReflection(bool bigEndian, bool wide, bool enabled)
        {
            int pointerSize = wide ? 8 : 4;
            string[] defines = enabled ? new[] { "EXTRA" } : Array.Empty<string>();
            LayoutManager registered = RegisteredScenarios.CreateLayouts(bigEndian, pointerSize)
                .RegisterTStruct<RegisteredScenarios.Optional>(defines);
            LayoutManager reflection = CreateReflectionLayouts(bigEndian, pointerSize, defines);
            RegisteredScenarios.ReadCustomModels(registered, bigEndian);
            RegisteredScenarios.ReadCustomModels(reflection, bigEndian);
            RegisteredScenarios.ReadOptionalFields(registered, bigEndian, enabled);
            RegisteredScenarios.ReadOptionalFields(reflection, bigEndian, enabled);

            Type[] models =
            {
                typeof(RegisteredScenarios.Ordered),
                typeof(RegisteredScenarios.Base),
                typeof(RegisteredScenarios.Derived),
                typeof(RegisteredScenarios.Empty),
                typeof(RegisteredScenarios.Nested),
                typeof(RegisteredScenarios.FixedArray),
                typeof(RegisteredScenarios.FixedPrimitiveArray),
                typeof(RegisteredScenarios.Optional),
                typeof(RegisteredScenarios.Node),
                typeof(RegisteredScenarios.NodePointer),
                typeof(Pointer<byte, ulong>),
                typeof(Pointer<byte, SizeT>)
            };
            foreach (Type model in models)
            {
                AssertEquivalentLayouts(reflection.GetLayout(model), registered.GetLayout(model));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MixRegisteredAndReflectionCustomLayouts(bool registerParent)
        {
            LayoutManager layouts = CreateReflectionLayouts(false, 4, Array.Empty<string>());
            if (registerParent)
            {
                layouts.RegisterTStruct<RegisteredScenarios.FixedArray>()
                    .RegisterArray<RegisteredScenarios.Ordered>();
            }
            else
            {
                layouts.RegisterTStruct<RegisteredScenarios.Ordered>();
            }
            MemoryBufferAddressSpace source = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
            RegisteredScenarios.FixedArray value = new Reader(source, layouts).Read<RegisteredScenarios.FixedArray>(0);
            Assert.Equal(2, value.Values.Length);
            Assert.Equal(0x04030201u, value.Values[0].First);
            Assert.Equal(0x0605, value.Values[0].Second);
            Assert.Equal(0x0c0b0a09u, value.Values[1].First);
            Assert.Equal(0x0e0d, value.Values[1].Second);
        }

        [Theory]
        [InlineData(false, 0u)]
        [InlineData(false, 1u)]
        [InlineData(false, 2u)]
        [InlineData(true, 0u)]
        [InlineData(true, 1u)]
        [InlineData(true, 2u)]
        public void CustomModelArraysMatchReflection(bool bigEndian, uint count)
        {
            LayoutManager registered = RegisteredScenarios.CreateLayouts(bigEndian, 4);
            LayoutManager reflection = CreateReflectionLayouts(bigEndian, 4, Array.Empty<string>());
            ILayout typedLayout = registered.GetArrayLayoutForElement<RegisteredScenarios.Ordered>(count);
            ILayout reflectionLayout = reflection.GetArrayLayout<RegisteredScenarios.Ordered[]>(count);
            Assert.Same(reflectionLayout, reflection.GetArrayLayout(typeof(RegisteredScenarios.Ordered[]), count));
            AssertEquivalentLayouts(reflectionLayout, typedLayout);
            MemoryBufferAddressSpace source = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 });
            foreach (ILayout layout in new[] { typedLayout, reflectionLayout })
            {
                RegisteredScenarios.Ordered[] values = Assert.IsType<RegisteredScenarios.Ordered[]>(layout.Read(source, 1, out uint bytesRead));
                Assert.Equal((int)count, values.Length);
                Assert.Equal(count * 8, bytesRead);
                for (int i = 0; i < values.Length; i++)
                {
                    Assert.Equal(bigEndian ? 0x02030405u + (uint)i * 0x08080808 : 0x05040302u + (uint)i * 0x08080808, values[i].First);
                    Assert.Equal(bigEndian ? 0x0607 + i * 0x0808 : 0x0706 + i * 0x0808, values[i].Second);
                }
                RegisteredScenarios.Ordered[] repeated = (RegisteredScenarios.Ordered[])layout.Read(source, 1);
                Assert.NotSame(values, repeated);
                for (int i = 0; i < values.Length; i++)
                {
                    Assert.NotSame(values[i], repeated[i]);
                }
            }
        }

        [Fact]
        public void ExtendPEVirtualAddressReader()
        {
            using Stream stream = File.OpenRead("TestBinaries/HelloWorld.exe");
            using PEFile pe = new(new StreamAddressSpace(stream));
            Reader reader = pe.RelativeVirtualAddressReader;
            Assert.Throws<LayoutException>(() => reader.LayoutManager.GetLayout<RegisteredScenarios.Ordered>());
            ulong position = pe.ImageDataDirectory[(int)ImageDirectoryEntry.Debug].VirtualAddress;
            RegisteredScenarios.ReadCustomHeader(reader, position);
        }

        [Theory]
        [InlineData("HelloWorld.pdb")]
        [InlineData("HelloWorld.pdz")]
        public void ExtendPDBStreamReader(string name)
        {
            using Stream stream = File.OpenRead(Path.Combine("TestBinaries", name));
            using PDBFile pdb = PDBFile.Open(new StreamAddressSpace(stream));
            RegisteredScenarios.ReadCustomHeader(pdb.GetStream(1));
        }

        [Fact]
        public void OptIntoLegacyDiscoveryOnParserReader()
        {
            using Stream stream = File.OpenRead("TestBinaries/HelloWorld.exe");
            using PEFile pe = new(new StreamAddressSpace(stream));
            Reader reader = pe.RelativeVirtualAddressReader;
            reader.LayoutManager.AddTStructTypes();
            ulong position = pe.ImageDataDirectory[(int)ImageDirectoryEntry.Debug].VirtualAddress;
            RegisteredScenarios.Ordered header = reader.Read<RegisteredScenarios.Ordered>(position);
            Assert.Equal(reader.Read<uint>(position), header.First);
            Assert.Equal(reader.Read<ushort>(position + 4), header.Second);
        }

        [Fact]
        public void NullAndEmptyDefinesUseMetadataOrder()
        {
            LayoutManager nullDefines = new LayoutManager().AddPrimitives()
                .RegisterTStruct<RegisteredScenarios.Ordered>(null);
            LayoutManager emptyDefines = new LayoutManager().AddPrimitives()
                .RegisterTStruct<RegisteredScenarios.Ordered>(Array.Empty<string>());
            string[] expected = { "First", "Second" };
            Assert.Equal(expected, nullDefines.GetLayout<RegisteredScenarios.Ordered>().Fields.Select(field => field.Name));
            Assert.Equal(expected, emptyDefines.GetLayout<RegisteredScenarios.Ordered>().Fields.Select(field => field.Name));
        }

        [Fact]
        public void RequireRegisteredDependencies()
        {
            LayoutManager layouts = new LayoutManager().AddPrimitives()
                .RegisterTStruct<RegisteredScenarios.Nested>()
                .RegisterTStruct<RegisteredScenarios.Derived>()
                .RegisterTStruct<RegisteredScenarios.FixedArray>();
            Assert.Throws<LayoutException>(() => layouts.GetLayout<RegisteredScenarios.Nested>());
            Assert.Throws<LayoutException>(() => layouts.GetLayout<RegisteredScenarios.Derived>());
            Assert.Throws<LayoutException>(() => layouts.GetLayout<RegisteredScenarios.FixedArray>());
            layouts.RegisterTStruct<RegisteredScenarios.Ordered>()
                .RegisterTStruct<RegisteredScenarios.Base>()
                .RegisterArray<RegisteredScenarios.Ordered>();
            Assert.Equal(8u, layouts.GetLayout<RegisteredScenarios.Nested>().Size);
            Assert.Equal(12u, layouts.GetLayout<RegisteredScenarios.Derived>().Size);
            Assert.Equal(16u, layouts.GetLayout<RegisteredScenarios.FixedArray>().Size);
        }

        [Fact]
        public void RejectDuplicateRegistrations()
        {
            LayoutManager layouts = new LayoutManager().AddPrimitives()
                .RegisterTStruct<RegisteredScenarios.Ordered>();
            Assert.Throws<ArgumentException>(() => layouts.RegisterTStruct<RegisteredScenarios.Ordered>());
            ILayout custom = new TLayout(typeof(RegisteredScenarios.Ordered), 8, 4, 8, Array.Empty<IField>());
            Assert.Throws<ArgumentException>(() => layouts.AddLayout(custom));
            LayoutManager existing = new();
            existing.AddLayout(custom);
            Assert.Throws<ArgumentException>(() => existing.RegisterTStruct<RegisteredScenarios.Ordered>());
            layouts.RegisterArray<byte>();
            Assert.Throws<ArgumentException>(() => layouts.RegisterArray<byte>());
            layouts.RegisterPointer<Pointer<byte, uint>, byte, uint>();
            Assert.Throws<ArgumentException>(() => layouts.RegisterPointer<Pointer<byte, uint>, byte, uint>());
            Assert.Throws<LayoutException>(() => layouts.RegisterPointer<Pointer<byte, short>, byte, short>());
        }

        [Fact]
        public void PreferRegistrationsAndCacheLayouts()
        {
            LayoutManager layouts = new LayoutManager().AddPrimitives()
                .RegisterTStruct<RegisteredScenarios.Ordered>();
            layouts.AddLayoutProvider((type, manager) => throw new InvalidOperationException("Unexpected provider"));
            ILayout layout = layouts.GetLayout<RegisteredScenarios.Ordered>();
            Assert.Same(layout, layouts.GetLayout<RegisteredScenarios.Ordered>());
            Assert.Same(layouts.GetArrayLayoutForElement<byte>(3), layouts.GetArrayLayoutForElement<byte>(3));
            Assert.Throws<ArgumentException>(() => layouts.RegisterTStruct<RegisteredScenarios.Ordered>());
        }

        [Fact]
        public void RejectOversizedArrayAllocations()
        {
            LayoutManager layouts = new LayoutManager().AddPrimitives();
            ILayout array = layouts.GetArrayLayoutForElement<byte>(uint.MaxValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => array.Read(new MemoryBufferAddressSpace(Array.Empty<byte>()), 0));
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(3u)]
        public void TypedAndLegacyArraysHaveEquivalentLayouts(uint count)
        {
            LayoutManager typed = new LayoutManager().AddPrimitives();
            LayoutManager legacy = new LayoutManager().AddPrimitives();
            ILayout typedLayout = typed.GetArrayLayoutForElement<byte>(count);
            ILayout legacyLayout = legacy.GetArrayLayout<byte[]>(count);
            Assert.Same(legacyLayout, legacy.GetArrayLayout(typeof(byte[]), count));
            Assert.Equal(legacyLayout.Type, typedLayout.Type);
            Assert.Equal(legacyLayout.Size, typedLayout.Size);
            Assert.Equal(legacyLayout.NaturalAlignment, typedLayout.NaturalAlignment);
            Assert.Equal(legacyLayout.SizeAsBaseType, typedLayout.SizeAsBaseType);

            MemoryBufferAddressSpace source = new(new byte[] { 0, 1, 2, 3 });
            byte[] expected = (byte[])legacyLayout.Read(source, 1, out uint legacyBytesRead);
            byte[] actual = (byte[])typedLayout.Read(source, 1, out uint typedBytesRead);
            Assert.Equal(expected, actual);
            Assert.Equal((int)count, actual.Length);
            Assert.Equal(legacyBytesRead, typedBytesRead);
            Assert.NotSame(actual, typedLayout.Read(source, 1));
        }

        [Fact]
        public void LegacyGenericArrayLayoutRejectsNonArrayTypes()
        {
            LayoutManager layouts = new LayoutManager().AddPrimitives();
            Assert.Throws<ArgumentException>(() => layouts.GetArrayLayout<byte>(3));
            Assert.Throws<ArgumentException>(() => layouts.GetArrayLayout<byte[,]>(3));
        }

        [Theory]
        [InlineData("PE", false, false)]
        [InlineData("PE", false, true)]
        [InlineData("ELF", false, false)]
        [InlineData("ELF", false, true)]
        [InlineData("ELF", true, false)]
        [InlineData("ELF", true, true)]
        [InlineData("Mach", false, false)]
        [InlineData("Mach", false, true)]
        [InlineData("Mach", true, false)]
        [InlineData("Mach", true, true)]
        [InlineData("MachFat", false, false)]
        [InlineData("MachFat", true, false)]
        [InlineData("Minidump", false, false)]
        [InlineData("Minidump", false, true)]
        [InlineData("Minidump", true, false)]
        [InlineData("Minidump", true, true)]
        [InlineData("PDB", false, false)]
        public void BuiltInLayoutsMatchReflection(string format, bool bigEndian, bool wide)
        {
            LayoutManager registered = new();
            LayoutManager legacy = new();
            string typeNamespace;
            switch (format)
            {
                case "PE":
                    registered.AddPETypes(wide);
                    legacy.AddPrimitives(false)
                        .AddEnumTypes()
                        .AddSizeT(wide ? 8 : 4)
                        .AddNullTerminatedString()
                        .AddTStructTypes(wide ? new[] { "PE32+" } : new[] { "PE32" });
                    typeNamespace = typeof(PEFile).Namespace;
                    break;
                case "ELF":
                    registered.AddELFTypes(bigEndian, wide);
                    legacy.AddPrimitives(bigEndian)
                        .AddEnumTypes()
                        .AddSizeT(wide ? 8 : 4)
                        .AddPointerTypes()
                        .AddNullTerminatedString()
                        .AddTStructTypes(wide ? new[] { "64BIT" } : new[] { "32BIT" });
                    typeNamespace = typeof(ELFFile).Namespace;
                    break;
                case "Mach":
                    registered.AddMachTypes(bigEndian, wide);
                    legacy.AddPrimitives(bigEndian)
                        .AddSizeT(wide ? 8 : 4)
                        .AddEnumTypes()
                        .AddNullTerminatedString()
                        .AddTStructTypes();
                    typeNamespace = typeof(MachOFile).Namespace;
                    break;
                case "MachFat":
                    registered.AddMachFatHeaderTypes(bigEndian);
                    legacy.AddPrimitives(bigEndian)
                        .AddEnumTypes()
                        .AddTStructTypes();
                    typeNamespace = typeof(MachOFile).Namespace;
                    break;
                case "Minidump":
                    registered.AddCrashDumpTypes(bigEndian, wide);
                    legacy.AddPrimitives(bigEndian)
                        .AddEnumTypes()
                        .AddSizeT(wide ? 8 : 4)
                        .AddPointerTypes()
                        .AddNullTerminatedString()
                        .AddTStructTypes();
                    typeNamespace = typeof(Minidump.Minidump).Namespace;
                    break;
                default:
                    registered.AddPDBTypes();
                    legacy.AddPrimitives().AddEnumTypes().AddTStructTypes();
                    typeNamespace = typeof(PDBFile).Namespace;
                    break;
            }

            Type[] models = typeof(TStruct).Assembly.GetTypes()
                .Where(type => type.Namespace == typeNamespace && typeof(TStruct).IsAssignableFrom(type)
                    && type.Name != "PdbChecksum"
                    && (format != "Mach" || !type.Name.StartsWith("MachFat", StringComparison.Ordinal))
                    && (format != "MachFat" || type.Name.StartsWith("MachFat", StringComparison.Ordinal))).ToArray();
            Assert.NotEmpty(models);
            foreach (Type model in models)
            {
                AssertEquivalentLayouts(legacy.GetLayout(model), registered.GetLayout(model));
            }
        }

        private static LayoutManager CreateReflectionLayouts(bool bigEndian, int pointerSize, string[] defines)
        {
            return new LayoutManager().AddPrimitives(bigEndian)
                .AddEnumTypes()
                .AddSizeT(pointerSize)
                .AddPointerTypes()
                .AddTStructTypes(defines);
        }

        private static void AssertEquivalentLayouts(ILayout expected, ILayout actual)
        {
            Assert.Equal(expected.Type, actual.Type);
            Assert.Equal(expected.IsFixedSize, actual.IsFixedSize);
            Assert.Equal(expected.Size, actual.Size);
            Assert.Equal(expected.SizeAsBaseType, actual.SizeAsBaseType);
            Assert.Equal(expected.NaturalAlignment, actual.NaturalAlignment);
            Assert.Equal(expected.Fields.Select(field => (field.Name, field.Offset, field.Layout.Type, field.Layout.Size, field.Layout.NaturalAlignment)),
                actual.Fields.Select(field => (field.Name, field.Offset, field.Layout.Type, field.Layout.Size, field.Layout.NaturalAlignment)));
        }
    }
}
