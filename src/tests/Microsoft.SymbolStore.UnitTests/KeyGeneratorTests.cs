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
    public class KeyGenerationTests
    {
        readonly ITracer _tracer;

        public KeyGenerationTests(ITestOutputHelper output)
        {
            _tracer = new Tracer(output);
        }

        [Fact]
        public void FileKeyGenerator()
        {
            ELFCoreKeyGeneratorInternal(fileGenerator: true);
            ELFFileKeyGeneratorInternal(fileGenerator: true);
            //MachCoreKeyGeneratorInternal(fileGenerator: true);
            MachOFileKeyGeneratorInternal(fileGenerator: true);
            MinidumpKeyGeneratorInternal(fileGenerator: true);
            PDBFileKeyGeneratorInternal(fileGenerator: true);
            PEFileKeyGeneratorInternal(fileGenerator: true);
            PortablePDBFileKeyGeneratorInternal(fileGenerator: true);
            PerfMapFileKeyGeneratorInternal(fileGenerator: true);
        }


        [Fact]
        public void PerfMapFileKeyGenerator()
        {
            PerfMapFileKeyGeneratorInternal(fileGenerator: false);
        }

        private void PerfMapFileKeyGeneratorInternal(bool fileGenerator)
        {
            const string LinuxPerfMapPath = "TestBinaries/PerfMapEnabled/System.ComponentModel.EventBasedAsync.ni.r2rmap";
            using (Stream linuxPerfMapStream = File.OpenRead(LinuxPerfMapPath))
            {
                var file = new SymbolStoreFile(linuxPerfMapStream, LinuxPerfMapPath);
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new PerfMapFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Single().Index == "system.componentmodel.eventbasedasync.ni.r2rmap/r2rmap-v1-734d59d6de0e96aa3c77b3e2ed498097/system.componentmodel.eventbasedasync.ni.r2rmap");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(!symbolKey.Any());

                IEnumerable<SymbolStoreKey> perfMapKey = generator.GetKeys(KeyTypeFlags.PerfMapKeys);
                Assert.True(!symbolKey.Any());

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(!symbolKey.Any());
            }
        }

        [Fact]
        public void ELFCoreKeyGenerator()
        {
            ELFCoreKeyGeneratorInternal(fileGenerator: false);
        }

        private void ELFCoreKeyGeneratorInternal(bool fileGenerator)
        {
            using (Stream core = TestUtilities.OpenCompressedFile("TestBinaries/triagedump.gz"))
            {
                var file = new SymbolStoreFile(core, "triagedump");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new ELFCoreKeyGenerator(_tracer, file);

                Dictionary<string, SymbolStoreKey> identityKeys = generator.GetKeys(KeyTypeFlags.IdentityKey).ToDictionary((key) => key.Index);
                Dictionary<string, SymbolStoreKey> symbolKeys = generator.GetKeys(KeyTypeFlags.SymbolKey).ToDictionary((key) => key.Index);
                Dictionary<string, SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys).ToDictionary((key) => key.Index);
                Dictionary<string, SymbolStoreKey> dacdbiKeys = generator.GetKeys(KeyTypeFlags.DacDbiKeys).ToDictionary((key) => key.Index);
                Dictionary<string, SymbolStoreKey> runtimeKeys = generator.GetKeys(KeyTypeFlags.RuntimeKeys).ToDictionary((key) => key.Index);

                // Program (SymbolTestApp2)
                Assert.True(identityKeys.ContainsKey("symboltestapp2.dll/DD52998F8000/symboltestapp2.dll"));
                Assert.True(symbolKeys.ContainsKey("symboltestapp2.pdb/ed4317cbcab24c1fa06d93f8164c74ddFFFFFFFF/symboltestapp2.pdb"));

                // System.IO.dll
                Assert.True(identityKeys.ContainsKey("system.io.dll/595CD90631400/system.io.dll"));
                Assert.True(symbolKeys.ContainsKey("system.io.pdb/5e949d2065c746a1b510de28f35d114cFFFFFFFF/system.io.pdb"));

                // System.Native.so
                Assert.True(identityKeys.ContainsKey("system.native.so/elf-buildid-3c22124b073eeb90746d6f6eab1ae2bf4097eb70/system.native.so"));
                Assert.True(symbolKeys.ContainsKey("_.debug/elf-buildid-sym-3c22124b073eeb90746d6f6eab1ae2bf4097eb70/_.debug"));

                // libcoreclr.so
                Assert.True(identityKeys.ContainsKey("libcoreclr.so/elf-buildid-8f39a52a756311ab365090bfe9edef7ee8c44503/libcoreclr.so"));
                Assert.True(symbolKeys.ContainsKey("_.debug/elf-buildid-sym-8f39a52a756311ab365090bfe9edef7ee8c44503/_.debug"));
                Assert.True(runtimeKeys.ContainsKey("libcoreclr.so/elf-buildid-8f39a52a756311ab365090bfe9edef7ee8c44503/libcoreclr.so"));

                Assert.True(clrKeys.ContainsKey("libmscordaccore.so/elf-buildid-coreclr-8f39a52a756311ab365090bfe9edef7ee8c44503/libmscordaccore.so"));
                Assert.True(clrKeys.ContainsKey("libsos.so/elf-buildid-coreclr-8f39a52a756311ab365090bfe9edef7ee8c44503/libsos.so"));
                Assert.True(clrKeys.ContainsKey("sos.netcore.dll/elf-buildid-coreclr-8f39a52a756311ab365090bfe9edef7ee8c44503/sos.netcore.dll"));

                Assert.True(dacdbiKeys.ContainsKey("libmscordaccore.so/elf-buildid-coreclr-8f39a52a756311ab365090bfe9edef7ee8c44503/libmscordaccore.so"));
                Assert.False(dacdbiKeys.ContainsKey("libsos.so/elf-buildid-coreclr-8f39a52a756311ab365090bfe9edef7ee8c44503/libsos.so"));
                Assert.False(dacdbiKeys.ContainsKey("sos.netcore.dll/elf-buildid-coreclr-8f39a52a756311ab365090bfe9edef7ee8c44503/sos.netcore.dll"));
            }
        }

        [Fact]
        public void ELFFileKeyGenerator()
        {
            ELFFileKeyGeneratorInternal(fileGenerator: false);
        }

        private void ELFFileKeyGeneratorInternal(bool fileGenerator)
        {
            using (Stream stream = TestUtilities.OpenCompressedFile("TestBinaries/libcoreclr.so.gz"))
            {
                var file = new SymbolStoreFile(stream, "libcoreclr.so");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new ELFFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "libcoreclr.so/elf-buildid-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/libcoreclr.so");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 1);
                Assert.True(symbolKey.First().Index == "_.debug/elf-buildid-sym-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/_.debug");

                Dictionary<string, SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys).ToDictionary((key) => key.Index);
                Assert.True(clrKeys.ContainsKey("libmscordaccore.so/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/libmscordaccore.so"));
                Assert.True(clrKeys.ContainsKey("libmscordbi.so/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/libmscordbi.so"));
                Assert.True(clrKeys.ContainsKey("mscordaccore.dll/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/mscordaccore.dll"));
                Assert.True(clrKeys.ContainsKey("mscordbi.dll/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/mscordbi.dll"));
                Assert.True(clrKeys.ContainsKey("libsos.so/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/libsos.so"));
                Assert.True(clrKeys.ContainsKey("sos.netcore.dll/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/sos.netcore.dll"));

                Dictionary<string, SymbolStoreKey> dacdbiKeys = generator.GetKeys(KeyTypeFlags.DacDbiKeys).ToDictionary((key) => key.Index);
                Assert.True(dacdbiKeys.ContainsKey("libmscordaccore.so/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/libmscordaccore.so"));
                Assert.True(dacdbiKeys.ContainsKey("libmscordbi.so/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/libmscordbi.so"));
                Assert.True(dacdbiKeys.ContainsKey("mscordaccore.dll/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/mscordaccore.dll"));
                Assert.True(dacdbiKeys.ContainsKey("mscordbi.dll/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/mscordbi.dll"));
                Assert.False(dacdbiKeys.ContainsKey("libsos.so/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/libsos.so"));
                Assert.False(dacdbiKeys.ContainsKey("sos.netcore.dll/elf-buildid-coreclr-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/sos.netcore.dll"));

                Dictionary<string, SymbolStoreKey> runtimeKeys = generator.GetKeys(KeyTypeFlags.RuntimeKeys).ToDictionary((key) => key.Index);
                Assert.True(runtimeKeys.ContainsKey("libcoreclr.so/elf-buildid-ef8f58a0b402d11c68f78342ef4fcc7d23798d4c/libcoreclr.so"));
            }

            using (Stream stream = TestUtilities.OpenCompressedFile("TestBinaries/libcoreclrtraceptprovider.so.dbg.gz"))
            {
                var file = new SymbolStoreFile(stream, "libcoreclrtraceptprovider.so.dbg");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new ELFFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "_.debug/elf-buildid-sym-ce4ce0558d878a05754dff246ccea2a70a1db3a8/_.debug");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 0);

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }

            using (Stream stream = File.OpenRead("TestBinaries/symbolized_executable"))
            {
                var file = new SymbolStoreFile(stream, "symbolized_executable");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new ELFFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "_.debug/elf-buildid-sym-126ba1461caf6644cfdd124bfcceeffa81b18897/_.debug");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 0);

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }

            using (Stream stream = File.OpenRead("TestBinaries/stripped_executable"))
            {
                var file = new SymbolStoreFile(stream, "stripped_executable");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new ELFFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "stripped_executable/elf-buildid-126ba1461caf6644cfdd124bfcceeffa81b18897/stripped_executable");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 1);
                Assert.True(symbolKey.First().Index == "_.debug/elf-buildid-sym-126ba1461caf6644cfdd124bfcceeffa81b18897/_.debug");

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }

            using (Stream stream = File.OpenRead("TestBinaries/md5_build_id"))
            {
                var file = new SymbolStoreFile(stream, "md5_build_id");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new ELFFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "md5_build_id/elf-buildid-001ba81f23966cf77e40bcbb0701cd3400000000/md5_build_id");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 1);
                Assert.True(symbolKey.First().Index == "_.debug/elf-buildid-sym-001ba81f23966cf77e40bcbb0701cd3400000000/_.debug");

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }
        }

        [Fact(Skip = "Need an alternate scheme to acquire the binary this test was reading")]
        public void MachCoreKeyGenerator()
        {
            MachCoreKeyGeneratorInternal(fileGenerator: false);
        }

        private void MachCoreKeyGeneratorInternal(bool fileGenerator)
        {
            using (Stream core = TestUtilities.DecompressFile("TestBinaries/core.gz", "TestBinaries/core"))
            {
                var file = new SymbolStoreFile(core, "core");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new MachCoreKeyGenerator(_tracer, file);

                Dictionary<string, SymbolStoreKey> identityKeys = generator.GetKeys(KeyTypeFlags.IdentityKey).ToDictionary((key) => key.Index);
                Dictionary<string, SymbolStoreKey> symbolKeys = generator.GetKeys(KeyTypeFlags.SymbolKey).ToDictionary((key) => key.Index);
                Dictionary<string, SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys).ToDictionary((key) => key.Index);
                Dictionary<string, SymbolStoreKey> dacdbiKeys = generator.GetKeys(KeyTypeFlags.DacDbiKeys).ToDictionary((key) => key.Index);
                Dictionary<string, SymbolStoreKey> runtimeKeys = generator.GetKeys(KeyTypeFlags.RuntimeKeys).ToDictionary((key) => key.Index);

                // System.Native.dylib
                Assert.True(identityKeys.ContainsKey("system.native.dylib/mach-uuid-f7c77509e13a3da18099a2b97e90fade/system.native.dylib"));
                Assert.True(symbolKeys.ContainsKey("_.dwarf/mach-uuid-sym-f7c77509e13a3da18099a2b97e90fade/_.dwarf"));

                // libcoreclr.dylib
                Assert.True(identityKeys.ContainsKey("libcoreclr.dylib/mach-uuid-3e0f66c5527338b18141e9d63b8ab415/libcoreclr.dylib"));
                Assert.True(symbolKeys.ContainsKey("_.dwarf/mach-uuid-sym-3e0f66c5527338b18141e9d63b8ab415/_.dwarf"));
                Assert.True(runtimeKeys.ContainsKey("libcoreclr.dylib/mach-uuid-3e0f66c5527338b18141e9d63b8ab415/libcoreclr.dylib"));

                Assert.True(clrKeys.ContainsKey("libmscordaccore.dylib/mach-uuid-coreclr-3e0f66c5527338b18141e9d63b8ab415/libmscordaccore.dylib"));
                Assert.True(clrKeys.ContainsKey("libmscordbi.dylib/mach-uuid-coreclr-3e0f66c5527338b18141e9d63b8ab415/libmscordbi.dylib"));
                Assert.True(clrKeys.ContainsKey("libsos.dylib/mach-uuid-coreclr-3e0f66c5527338b18141e9d63b8ab415/libsos.dylib"));
                Assert.True(clrKeys.ContainsKey("sos.netcore.dll/mach-uuid-coreclr-3e0f66c5527338b18141e9d63b8ab415/sos.netcore.dll"));

                Assert.True(dacdbiKeys.ContainsKey("libmscordaccore.dylib/mach-uuid-coreclr-3e0f66c5527338b18141e9d63b8ab415/libmscordaccore.dylib"));
                Assert.True(dacdbiKeys.ContainsKey("libmscordbi.dylib/mach-uuid-coreclr-3e0f66c5527338b18141e9d63b8ab415/libmscordbi.dylib"));
                Assert.False(dacdbiKeys.ContainsKey("libsos.dylib/mach-uuid-coreclr-3e0f66c5527338b18141e9d63b8ab415/libsos.dylib"));
                Assert.False(dacdbiKeys.ContainsKey("sos.netcore.dll/mach-uuid-coreclr-3e0f66c5527338b18141e9d63b8ab415/sos.netcore.dll"));

            }
        }

        [Fact]
        public void MachOFileKeyGenerator()
        {
            MachOFileKeyGeneratorInternal(fileGenerator: false);
        }

        private void MachOFileKeyGeneratorInternal(bool fileGenerator)
        {
            using (Stream dylib = TestUtilities.OpenCompressedFile("TestBinaries/libcoreclr.dylib.gz"))
            {
                var file = new SymbolStoreFile(dylib, "libcoreclr.dylib");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new MachOFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "libcoreclr.dylib/mach-uuid-b5372bdabccd38f8899b6a782ceca847/libcoreclr.dylib");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 1);
                Assert.True(symbolKey.First().Index == "_.dwarf/mach-uuid-sym-b5372bdabccd38f8899b6a782ceca847/_.dwarf");

                Dictionary<string, SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys).ToDictionary((key) => key.Index);
                Assert.True(clrKeys.ContainsKey("libmscordaccore.dylib/mach-uuid-coreclr-b5372bdabccd38f8899b6a782ceca847/libmscordaccore.dylib"));
                Assert.True(clrKeys.ContainsKey("libsos.dylib/mach-uuid-coreclr-b5372bdabccd38f8899b6a782ceca847/libsos.dylib"));
                Assert.True(clrKeys.ContainsKey("sos.netcore.dll/mach-uuid-coreclr-b5372bdabccd38f8899b6a782ceca847/sos.netcore.dll"));

                Dictionary<string, SymbolStoreKey> runtimeKeys = generator.GetKeys(KeyTypeFlags.RuntimeKeys).ToDictionary((key) => key.Index);
                Assert.True(runtimeKeys.ContainsKey("libcoreclr.dylib/mach-uuid-b5372bdabccd38f8899b6a782ceca847/libcoreclr.dylib"));
            }

            using (Stream dwarf = TestUtilities.OpenCompressedFile("TestBinaries/libclrjit.dylib.dwarf.gz"))
            {
                var file = new SymbolStoreFile(dwarf, "libclrjit.dylib.dwarf");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new MachOFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "_.dwarf/mach-uuid-sym-b35e230c8ee932efb6e6e6ed18a604a8/_.dwarf");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 0);

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }

            using (Stream machofat = TestUtilities.OpenCompressedFile("TestBinaries/libSystem.Security.Cryptography.Native.Apple.dylib.gz"))
            {
                var file = new SymbolStoreFile(machofat, "libsystem.security.cryptography.native.apple.dylib");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new MachOFatHeaderKeyGenerator(_tracer, file);

                Dictionary<string, SymbolStoreKey> identityKeys = generator.GetKeys(KeyTypeFlags.IdentityKey).ToDictionary((key) => key.Index);
                Assert.True(identityKeys.ContainsKey("libsystem.security.cryptography.native.apple.dylib/mach-uuid-fad93e41f2e23d11aab75e98d7fe66d6/libsystem.security.cryptography.native.apple.dylib"));
                Assert.True(identityKeys.ContainsKey("libsystem.security.cryptography.native.apple.dylib/mach-uuid-e5bf8b935f393806a20933aa98adf5b7/libsystem.security.cryptography.native.apple.dylib"));

                Dictionary<string, SymbolStoreKey> symbolKeys = generator.GetKeys(KeyTypeFlags.SymbolKey).ToDictionary((key) => key.Index);
                Assert.True(symbolKeys.ContainsKey("_.dwarf/mach-uuid-sym-fad93e41f2e23d11aab75e98d7fe66d6/_.dwarf"));
                Assert.True(symbolKeys.ContainsKey("_.dwarf/mach-uuid-sym-e5bf8b935f393806a20933aa98adf5b7/_.dwarf"));
            }
        }

        [Fact]
        public void MinidumpKeyGenerator()
        {
            MinidumpKeyGeneratorInternal(fileGenerator: false);
        }

        private void MinidumpKeyGeneratorInternal(bool fileGenerator)
        {
            using (Stream core = TestUtilities.OpenCompressedFile("TestBinaries/minidump_x64.dmp.gz"))
            {
                var file = new SymbolStoreFile(core, "minidump_x64.dmp");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new MinidumpKeyGenerator(_tracer, file);

                Dictionary<string, SymbolStoreKey> identityKeys = generator.GetKeys(KeyTypeFlags.IdentityKey).ToDictionary((key) => key.Index);
                Dictionary<string, SymbolStoreKey> symbolKeys = generator.GetKeys(KeyTypeFlags.SymbolKey).ToDictionary((key) => key.Index);

                // Program (exception.exe)
                Assert.True(identityKeys.ContainsKey("exception.exe/57B39FFA6000/exception.exe"));
                Assert.True(symbolKeys.ContainsKey("exception.pdb/df85e94d63ae4d8992fbf81730a7ac911/exception.pdb"));

                // mscoree.dll
                Assert.True(identityKeys.ContainsKey("mscoree.dll/57A5832766000/mscoree.dll"));
                Assert.True(symbolKeys.ContainsKey("mscoree.pdb/4a348372fdff448ab6a1bfc8b93ffb6b1/mscoree.pdb"));
            }
        }

        [Fact]
        public void PDBFileKeyGenerator()
        {
            PDBFileKeyGeneratorInternal(fileGenerator: false);
        }

        private void PDBFileKeyGeneratorInternal(bool fileGenerator)
        {
            const string TestBinary = "TestBinaries/HelloWorld.pdb";
            using (Stream pdb = File.OpenRead(TestBinary))
            {
                var file = new SymbolStoreFile(pdb, TestBinary);
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new PDBFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "helloworld.pdb/99891b3ed7ae4c3babff8a2b4a9b0c431/helloworld.pdb");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 0);

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }
        }

        [Fact]
        public void PEFileKeyGenerator()
        {
            PEFileKeyGeneratorInternal(fileGenerator: false);
        }

        private void PEFileKeyGeneratorInternal(bool fileGenerator)
        {
            const string TestBinaryExe = "TestBinaries/HelloWorld.exe";
            using (Stream exe = File.OpenRead(TestBinaryExe))
            {
                var file = new SymbolStoreFile(exe, TestBinaryExe);
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new PEFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "helloworld.exe/577F59198000/helloworld.exe");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 1);
                Assert.True(symbolKey.First().Index == "helloworld.pdb/99891b3ed7ae4c3babff8a2b4a9b0c431/helloworld.pdb");

                IEnumerable<SymbolStoreKey> perfMapKey = generator.GetKeys(KeyTypeFlags.PerfMapKeys);
                Assert.True(perfMapKey.Count() == 0);

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }

            const string LinuxPePath = "TestBinaries/PerfMapEnabled/System.ComponentModel.EventBasedAsync.dll";
            using (Stream linuxPeStream = File.OpenRead(LinuxPePath))
            {
                var file = new SymbolStoreFile(linuxPeStream, LinuxPePath);
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new PEFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "system.componentmodel.eventbasedasync.dll/9757F3A636c00/system.componentmodel.eventbasedasync.dll");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Single().Index == "system.componentmodel.eventbasedasync.pdb/99d3f272c6a8429ba694647a7912d178FFFFFFFF/system.componentmodel.eventbasedasync.pdb");

                IEnumerable<SymbolStoreKey> perfMapKey = generator.GetKeys(KeyTypeFlags.PerfMapKeys);
                Assert.True(perfMapKey.Single().Index == "system.componentmodel.eventbasedasync.ni.r2rmap/r2rmap-v1-734d59d6de0e96aa3c77b3e2ed498097/system.componentmodel.eventbasedasync.ni.r2rmap");

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }

            const string TestBinaryDll = "TestBinaries/System.Diagnostics.StackTrace.dll";
            using (Stream dll = File.OpenRead(TestBinaryDll))
            {
                var file = new SymbolStoreFile(dll, TestBinaryDll);
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new PEFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "system.diagnostics.stacktrace.dll/595CD91B35a00/system.diagnostics.stacktrace.dll");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 2);
                Assert.True(symbolKey.First().Index == "system.diagnostics.stacktrace.ni.pdb/3cd5a68a9f2cd99b169d074e6e956d4fFFFFFFFF/system.diagnostics.stacktrace.ni.pdb");
                Assert.True(symbolKey.Last().Index == "system.diagnostics.stacktrace.pdb/8b2e8cf443144806982ab7d904876a50FFFFFFFF/system.diagnostics.stacktrace.pdb");

                IEnumerable<SymbolStoreKey> perfMapKey = generator.GetKeys(KeyTypeFlags.PerfMapKeys);
                Assert.True(perfMapKey.Count() == 0);

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }

            using (Stream coreclr = TestUtilities.OpenCompressedFile("TestBinaries/coreclr.dll.gz"))
            {
                var file = new SymbolStoreFile(coreclr, "coreclr.dll");
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new PEFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "coreclr.dll/595EBCD5538000/coreclr.dll");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 1);
                Assert.True(symbolKey.First().Index == "coreclr.pdb/3f3d5a3258e64ae8b86b31ff776949351/coreclr.pdb");

                Dictionary<string, SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys).ToDictionary((key) => key.Index);
                Assert.True(clrKeys.Count() == 3);
                Assert.True(clrKeys.ContainsKey("mscordaccore.dll/595EBCD5538000/mscordaccore.dll"));
                Assert.True(clrKeys.ContainsKey("mscordaccore_amd64_amd64_4.6.25505.00.dll/595EBCD5538000/mscordaccore_amd64_amd64_4.6.25505.00.dll"));
                Assert.True(clrKeys.ContainsKey("mscordbi.dll/595EBCD5538000/mscordbi.dll"));

                Dictionary<string, SymbolStoreKey> dacdbiKeys = generator.GetKeys(KeyTypeFlags.DacDbiKeys).ToDictionary((key) => key.Index);
                Assert.True(dacdbiKeys.Count() == 3);
                Assert.True(dacdbiKeys.ContainsKey("mscordaccore.dll/595EBCD5538000/mscordaccore.dll"));
                Assert.True(dacdbiKeys.ContainsKey("mscordaccore_amd64_amd64_4.6.25505.00.dll/595EBCD5538000/mscordaccore_amd64_amd64_4.6.25505.00.dll"));
                Assert.True(dacdbiKeys.ContainsKey("mscordbi.dll/595EBCD5538000/mscordbi.dll"));

                IEnumerable<SymbolStoreKey> perfMapKey = generator.GetKeys(KeyTypeFlags.PerfMapKeys);
                Assert.True(perfMapKey.Count() == 0);

                Dictionary<string, SymbolStoreKey> runtimeKeys = generator.GetKeys(KeyTypeFlags.RuntimeKeys).ToDictionary((key) => key.Index);
                Assert.True(runtimeKeys.ContainsKey("coreclr.dll/595EBCD5538000/coreclr.dll"));
            }
        }

        [Fact]
        public void PortablePDBFileKeyGenerator()
        {
            PortablePDBFileKeyGeneratorInternal(fileGenerator: false);
        }

        private void PortablePDBFileKeyGeneratorInternal(bool fileGenerator)
        {
            const string TestBinary = "TestBinaries/dir1/System.Threading.Thread.pdb";
            using (Stream pdb = File.OpenRead(TestBinary))
            {
                var file = new SymbolStoreFile(pdb, TestBinary);
                KeyGenerator generator = fileGenerator ? (KeyGenerator)new FileKeyGenerator(_tracer, file) : new PortablePDBFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "system.threading.thread.pdb/a43b38726e6a4b3cb1691f35f0d6cc48FFFFFFFF/system.threading.thread.pdb");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 0);

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }
        }

        [Fact]
        public void SourceFileKeyGenerator()
        {
            using (Stream source = TestUtilities.OpenCompressedFile("TestBinaries/StackTraceSymbols.CoreCLR.cs.gz"))
            {
                var file = new SymbolStoreFile(source, "StackTraceSymbols.CoreCLR.cs");
                var generator = new SourceFileKeyGenerator(_tracer, file);

                IEnumerable<SymbolStoreKey> identityKey = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(identityKey.Count() == 1);
                Assert.True(identityKey.First().Index == "stacktracesymbols.coreclr.cs/sha1-da39a3ee5e6b4b0d3255bfef95601890afd80709/stacktracesymbols.coreclr.cs");

                IEnumerable<SymbolStoreKey> symbolKey = generator.GetKeys(KeyTypeFlags.SymbolKey);
                Assert.True(symbolKey.Count() == 0);

                IEnumerable<SymbolStoreKey> clrKeys = generator.GetKeys(KeyTypeFlags.ClrKeys);
                Assert.True(clrKeys.Count() == 0);
            }
        }
    }
}
