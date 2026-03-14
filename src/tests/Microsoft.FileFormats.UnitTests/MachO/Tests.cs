// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using TestHelpers;
using Xunit;

namespace Microsoft.FileFormats.MachO.Tests
{
    public class Tests
    {
        [Fact]
        public void CheckIndexingInfo()
        {
            // https://dotnet.myget.org/feed/dotnet-core/package/nuget/runtime.osx.10.12-x64.Microsoft.NETCore.Runtime.CoreCLR/1.1.2
            using (Stream dylib = TestUtilities.OpenCompressedFile("TestBinaries/libcoreclr.dylib.gz"))
            {
                StreamAddressSpace dataSource = new(dylib);
                MachOFile machO = new(dataSource);
                Assert.True(machO.IsValid());
                Assert.Equal(Guid.Parse("da2b37b5-cdbc-f838-899b-6a782ceca847"), new Guid(machO.Uuid));
            }
        }

        [Fact]
        public void CheckDwarfIndexingInfo()
        {
            // From a local build
            using (Stream dwarf = TestUtilities.OpenCompressedFile("TestBinaries/libclrjit.dylib.dwarf.gz"))
            {
                StreamAddressSpace dataSource = new(dwarf);
                MachOFile machO = new(dataSource);
                Assert.True(machO.IsValid());
                Assert.Equal(Guid.Parse("0c235eb3-e98e-ef32-b6e6-e6ed18a604a8"), new Guid(machO.Uuid));
            }
        }

        [Fact(Skip = "Need an alternate scheme to acquire the binary this test was reading")]
        public void ParseCore()
        {
            using (Stream core = TestUtilities.DecompressFile("TestBinaries/core.gz", "TestBinaries/core"))
            {
                StreamAddressSpace dataSource = new(core);
                // hard-coding the dylinker position so we don't pay to search for it each time
                // the code is capable of finding it by brute force search even if we don't provide the hint
                MachCore coreReader = new(dataSource, 0x000000010750c000);
                Assert.True(coreReader.IsValid());
                MachLoadedImage[] images = coreReader.LoadedImages.Where(i => i.Path.EndsWith("libcoreclr.dylib")).ToArray();
                MachOFile libCoreclr = images[0].Image;
                Assert.True(libCoreclr.IsValid());
                Assert.Equal(Guid.Parse("c5660f3e-7352-b138-8141-e9d63b8ab415"), new Guid(libCoreclr.Uuid));
            }
        }
    }
}
