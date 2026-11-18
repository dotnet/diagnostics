// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.Minidump;
using Microsoft.FileFormats.PDB;
using Microsoft.FileFormats.PE;

namespace Microsoft.FileFormats.Tests
{
    internal static class Program
    {
        private static void Main()
        {
            RegisteredScenarios.Run();
            VerifyBuiltInLayouts();
            using (Stream stream = OpenFixture("HelloWorld.exe"))
            {
                PEFile pe = new(new StreamAddressSpace(stream));
                RegisteredScenarios.Check(pe.IsValid() && pe.SizeOfImage == 0x8000, "PE image");
                RegisteredScenarios.Check(pe.Pdbs.Single().Signature == Guid.Parse("99891b3e-d7ae-4c3b-abff-8a2b4a9b0c43"), "PE debug directory");
                RegisteredScenarios.ReadCustomHeader(pe.RelativeVirtualAddressReader, pe.ImageDataDirectory[(int)ImageDirectoryEntry.Debug].VirtualAddress);
            }
            foreach (string name in new[] { "HelloWorld.pdb", "HelloWorld.pdz" })
            {
                using Stream stream = OpenFixture(name);
                PDBFile pdb = PDBFile.Open(new StreamAddressSpace(stream));
                RegisteredScenarios.Check(pdb.Age == 1 && pdb.Signature == Guid.Parse("99891b3e-d7ae-4c3b-abff-8a2b4a9b0c43"), name);
                RegisteredScenarios.ReadCustomHeader(pdb.GetStream(1));
            }
            foreach ((string name, string buildId) in new[] {
                ("libcoreclr.so.gz", "EF8F58A0B402D11C68F78342EF4FCC7D23798D4C"),
                ("apphost.gz", "316D55471A8D5EBD6F2CB0631F0020518AB13DC0") })
            {
                using Stream stream = OpenFixture(name);
                ELFFile elf = new(new StreamAddressSpace(stream));
                RegisteredScenarios.Check(elf.IsValid() && Convert.ToHexString(elf.BuildID) == buildId, name);
            }
            using (Stream stream = OpenFixture("libcoreclr.dylib.gz"))
            {
                MachOFile mach = new(new StreamAddressSpace(stream));
                RegisteredScenarios.Check(mach.IsValid() && new Guid(mach.Uuid) == Guid.Parse("da2b37b5-cdbc-f838-899b-6a782ceca847"), "Mach-O");
            }
            foreach ((string name, string signature) in new[] {
                ("minidump_x86.dmp.gz", "df1e3528-29be-4d0e-9457-4c8ccfdc278a"),
                ("minidump_x64.dmp.gz", "e18d6461-eb4f-49a6-b418-e9af91007a21") })
            {
                using Stream stream = OpenFixture(name);
                StreamAddressSpace source = new(stream);
                RegisteredScenarios.Check(Minidump.Minidump.IsValid(source), name);
                Minidump.Minidump dump = new(source);
                PEFile clr = dump.LoadedImages.Single(image => image.ModuleName.EndsWith(@"\clr.dll", StringComparison.Ordinal)).Image;
                RegisteredScenarios.Check(clr.Pdbs.Single().Signature == Guid.Parse(signature), "Minidump nested PE");
            }
            Console.WriteLine("Registered custom layouts and PE, ELF, Mach-O, minidump, PDB, and MSFZ parsers passed.");
        }

        private static void VerifyBuiltInLayouts()
        {
            foreach (bool bigEndian in new[] { false, true })
            {
                foreach (bool wide in new[] { false, true })
                {
                    VerifyLayouts(new LayoutManager().AddELFTypes(bigEndian, wide));
                    VerifyLayouts(new LayoutManager().AddMachTypes(bigEndian, wide));
                    VerifyLayouts(new LayoutManager().AddCrashDumpTypes(bigEndian, wide));
                }
                VerifyLayouts(new LayoutManager().AddMachFatHeaderTypes(bigEndian));
            }
            VerifyLayouts(new LayoutManager().AddPETypes(false));
            VerifyLayouts(new LayoutManager().AddPETypes(true));
            VerifyLayouts(new LayoutManager().AddPDBTypes());
        }

        private static void VerifyLayouts(LayoutManager layouts)
        {
            // Enumerate registrations without Assembly.GetTypes, which cannot guarantee complete metadata under trimming.
            FieldInfo registry = typeof(LayoutManager).GetField("_layoutFactories", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Layout factory registry was not found.");
            Dictionary<Type, Func<LayoutManager, ILayout>> factories = (Dictionary<Type, Func<LayoutManager, ILayout>>)registry.GetValue(layouts);
            foreach (Type type in factories.Keys)
            {
                ILayout layout = layouts.GetLayout(type);
                object instance = layout.Read(new MemoryBufferAddressSpace(new byte[checked((int)layout.Size)]), 0);
                RegisteredScenarios.Check(instance.GetType() == type, "Built-in construction: " + type.FullName);
                foreach (IField field in layout.Fields)
                {
                    RegisteredScenarios.Check(field.Offset + field.Layout.Size <= layout.Size, "Built-in field bounds: " + field.Name);
                }
            }
        }

        private static Stream OpenFixture(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "TestBinaries", name);
            if (!name.EndsWith(".gz", StringComparison.Ordinal))
            {
                return File.OpenRead(path);
            }
            using FileStream file = File.OpenRead(path);
            using GZipStream compressed = new(file, CompressionMode.Decompress);
            MemoryStream result = new();
            compressed.CopyTo(result);
            result.Position = 0;
            return result;
        }
    }
}
