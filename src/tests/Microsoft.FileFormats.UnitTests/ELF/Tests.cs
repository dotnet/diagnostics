// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Linq;
using TestHelpers;
using Xunit;

namespace Microsoft.FileFormats.ELF.Tests
{
    public class Tests
    {
        [Fact]
        public void CheckIndexingInfo()
        {
            using (Stream libcoreclr = TestUtilities.OpenCompressedFile("TestBinaries/libcoreclr.so.gz"))
            {
                StreamAddressSpace dataSource = new(libcoreclr);
                ELFFile elf = new(dataSource);
                Assert.True(elf.IsValid());
                Assert.True(elf.Header.Type == ELFHeaderType.Shared);
                string buildId = TestUtilities.ToHexString(elf.BuildID);

                //this is the build id for libcoreclr.so from package:
                // https://dotnet.myget.org/feed/dotnet-core/package/nuget/runtime.ubuntu.14.04-x64.Microsoft.NETCore.Runtime.CoreCLR/2.0.0-preview3-25428-01
                Assert.Equal("ef8f58a0b402d11c68f78342ef4fcc7d23798d4c", buildId);
            }

            // 32 bit arm ELF binary
            using (Stream apphost = TestUtilities.OpenCompressedFile("TestBinaries/apphost.gz"))
            {
                StreamAddressSpace dataSource = new(apphost);
                ELFFile elf = new(dataSource);
                Assert.True(elf.IsValid());
                Assert.True(elf.Header.Type == ELFHeaderType.Executable);
                string buildId = TestUtilities.ToHexString(elf.BuildID);

                //this is the build id for apphost from package:
                // https://dotnet.myget.org/F/dotnet-core/symbols/runtime.linux-arm.Microsoft.NETCore.DotNetAppHost/2.1.0-preview2-25512-03
                Assert.Equal("316d55471a8d5ebd6f2cb0631f0020518ab13dc0", buildId);
            }
        }

        [Fact]
        public void CheckDbgIndexingInfo()
        {
            using (Stream stream = TestUtilities.OpenCompressedFile("TestBinaries/libcoreclrtraceptprovider.so.dbg.gz"))
            {
                StreamAddressSpace dataSource = new(stream);
                ELFFile elf = new(dataSource);
                Assert.True(elf.IsValid());
                Assert.True(elf.Header.Type == ELFHeaderType.Shared);
                string buildId = TestUtilities.ToHexString(elf.BuildID);
                Assert.Equal("ce4ce0558d878a05754dff246ccea2a70a1db3a8", buildId);
            }
        }

        [Fact]
        public void CheckFreeBSDIndexingInfo()
        {
            using (Stream stream = File.OpenRead("TestBinaries/ilasm.dbg"))
            {
                StreamAddressSpace dataSource = new(stream);
                ELFFile elf = new(dataSource);
                Assert.True(elf.IsValid());
                Assert.True(elf.Header.Type == ELFHeaderType.Executable);
                string buildId = TestUtilities.ToHexString(elf.BuildID);
                Assert.Equal("4a91e41002a1307ef4097419d7875df001969daa", buildId);
            }
        }

        [Fact]
        public void CheckCustomNamedBuildIdSection()
        {
            using (Stream stream = File.OpenRead("TestBinaries/renamed_build_id_section"))
            {
                StreamAddressSpace dataSource = new(stream);
                ELFFile elf = new(dataSource);
                Assert.True(elf.IsValid());
                Assert.True(elf.Header.Type == ELFHeaderType.Shared);
                string buildId = TestUtilities.ToHexString(elf.BuildID);
                Assert.Equal("1bd6a199dcb6f234558d9439cfcbba2727f1e1d9", buildId);
            }
        }

        [Fact]
        public void ParseCore()
        {
            using (Stream core = TestUtilities.OpenCompressedFile("TestBinaries/core.gz"))
            {
                StreamAddressSpace dataSource = new(core);
                ELFCoreFile coreReader = new(dataSource);
                Assert.True(coreReader.IsValid());
                ELFLoadedImage loadedImage = coreReader.LoadedImages.Where(i => i.Path.EndsWith("librt-2.17.so")).First();
                Assert.True(loadedImage.Image.IsValid());
                Assert.True(loadedImage.Image.Header.Type == ELFHeaderType.Shared);
                string buildId = TestUtilities.ToHexString(loadedImage.Image.BuildID);
                Assert.Equal("1d2ad4eaa62bad560685a4b8dccc8d9aa95e22ce", buildId);
            }
        }

        [Fact]
        public void ParseTriageDump()
        {
            using (Stream core = TestUtilities.OpenCompressedFile("TestBinaries/triagedump.gz"))
            {
                StreamAddressSpace dataSource = new(core);
                ELFCoreFile coreReader = new(dataSource);
                Assert.True(coreReader.IsValid());
                ELFLoadedImage loadedImage = coreReader.LoadedImages.Where(i => i.Path.EndsWith("libcoreclr.so")).First();
                Assert.True(loadedImage.Image.IsValid());
                Assert.True(loadedImage.Image.Header.Type == ELFHeaderType.Shared);
                string buildId = TestUtilities.ToHexString(loadedImage.Image.BuildID);
                Assert.Equal("8f39a52a756311ab365090bfe9edef7ee8c44503", buildId);
            }
        }
    }
}
