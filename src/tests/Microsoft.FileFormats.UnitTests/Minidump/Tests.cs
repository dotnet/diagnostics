// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.FileFormats.PE;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using TestHelpers;
using Xunit;

namespace Microsoft.FileFormats.Minidump
{
    public class Tests
    {
        const string x86Dump = "TestBinaries/minidump_x86.dmp.gz";
        const string x64Dump = "TestBinaries/minidump_x64.dmp.gz";

        readonly Guid x64ClrGuid = new Guid("e18d6461-eb4f-49a6-b418-e9af91007a21");
        readonly Guid x86ClrGuid = new Guid("df1e3528-29be-4d0e-9457-4c8ccfdc278a");
        const int ClrAge = 2;
        const string ClrPdb = "clr.pdb";

        [Fact]
        public void CheckIsMinidump()
        {
            using (Stream stream = TestUtilities.OpenCompressedFile(x86Dump))
            {
                Assert.True(Minidump.IsValid(new StreamAddressSpace(stream)));
                Assert.False(Minidump.IsValid(new StreamAddressSpace(stream), 1));
            }

            using (Stream stream = TestUtilities.OpenCompressedFile(x64Dump))
            {
                Assert.True(Minidump.IsValid(new StreamAddressSpace(stream)));
                Assert.False(Minidump.IsValid(new StreamAddressSpace(stream), 1));
            }

            // These are GZiped files, they should not be minidumps.
            using (FileStream stream = File.OpenRead(x86Dump))
                Assert.False(Minidump.IsValid(new StreamAddressSpace(stream)));

            using (FileStream stream = File.OpenRead(x64Dump))
                Assert.False(Minidump.IsValid(new StreamAddressSpace(stream)));
        }

        [Fact]
        public void CheckPdbInfo()
        {
            using (Stream stream = TestUtilities.OpenCompressedFile(x86Dump))
            {
                CheckPdbInfoInternal(GetMinidumpFromStream(stream), x86ClrGuid);
            }
            using (Stream stream = TestUtilities.OpenCompressedFile(x64Dump))
            {
                CheckPdbInfoInternal(GetMinidumpFromStream(stream), x64ClrGuid);
            }
        }

        private void CheckPdbInfoInternal(Minidump minidump, Guid guid)
        {
            PEFile image = minidump.LoadedImages.Where(i => i.ModuleName.EndsWith(@"\clr.dll")).Single().Image;
            foreach (PEPdbRecord pdb in image.Pdbs)
            {
                Assert.NotNull(pdb);
                Assert.Equal(ClrPdb, pdb.Path);
                Assert.Equal(ClrAge, pdb.Age);
                Assert.Equal(guid, pdb.Signature);
            }
        }

        [Fact]
        public void CheckModuleNames()
        {
            using (Stream stream = TestUtilities.OpenCompressedFile(x86Dump))
            {
                CheckModuleNamesInternal(GetMinidumpFromStream(stream));
            }
            using (Stream stream = TestUtilities.OpenCompressedFile(x64Dump))
            {
                CheckModuleNamesInternal(GetMinidumpFromStream(stream));
            }
        }

        private void CheckModuleNamesInternal(Minidump minidump)
        {
            Assert.Single(minidump.LoadedImages.Where(i => i.ModuleName.EndsWith(@"\clr.dll")));

            foreach (var module in minidump.LoadedImages)
                Assert.NotNull(module.ModuleName);
        }

        [Fact]
        public void CheckNestedPEImages()
        {
            using (Stream stream = TestUtilities.OpenCompressedFile(x86Dump))
            {
                CheckNestedPEImagesInternal(GetMinidumpFromStream(stream));
            }
            using (Stream stream = TestUtilities.OpenCompressedFile(x64Dump))
            {
                CheckNestedPEImagesInternal(GetMinidumpFromStream(stream));
            }
        }

        private void CheckNestedPEImagesInternal(Minidump minidump)
        {
            foreach (var loadedImage in minidump.LoadedImages)
            {
                Assert.True(loadedImage.Image.HasValidDosSignature.Check());
                Assert.True(loadedImage.Image.HasValidPESignature.Check());
            }
        }

        [Fact]
        public void CheckMemoryRanges()
        {
            using (Stream stream = TestUtilities.OpenCompressedFile(x86Dump))
            {
                CheckMemoryRangesInternal(GetMinidumpFromStream(stream));
            }
            using (Stream stream = TestUtilities.OpenCompressedFile(x64Dump))
            {
                CheckMemoryRangesInternal(GetMinidumpFromStream(stream));
            }
        }

        private void CheckMemoryRangesInternal(Minidump minidump)
        {
            ReadOnlyCollection<MinidumpLoadedImage> images = minidump.LoadedImages;
            ReadOnlyCollection<MinidumpSegment> memory = minidump.Segments;
            
            // Ensure that all of our images actually correspond to memory in the crash dump.  Note that our minidumps used
            // for this test are all full dumps with all memory (including images) in them.
            foreach (var image in images)
            {
                int count = memory.Where(m => m.VirtualAddress <= image.BaseAddress && image.BaseAddress < m.VirtualAddress + m.Size).Count();
                Assert.Equal(1, count);
                
                // Check the start of each image for the PE header 'MZ'
                byte[] header = minidump.VirtualAddressReader.Read(image.BaseAddress, 2);
                Assert.Equal((byte)'M', header[0]);
                Assert.Equal((byte)'Z', header[1]);
            }
        }

        [Fact]
        public void CheckLoadedModules()
        {
            using (Stream stream = TestUtilities.OpenCompressedFile(x86Dump))
            {
                CheckLoadedModulesInternal(stream);
            }
            using (Stream stream = TestUtilities.OpenCompressedFile(x64Dump))
            {
                CheckLoadedModulesInternal(stream);
            }
        }

        private static void CheckLoadedModulesInternal(Stream stream)
        {
            Minidump minidump = GetMinidumpFromStream(stream);

            var modules = minidump.LoadedImages;
            Assert.True(modules.Count > 0);
        }

        [Fact]
        public void CheckStartupMemoryRead()
        {
            using (Stream stream = TestUtilities.OpenCompressedFile(x86Dump))
            {
                CheckStartupMemoryReadInternal(stream);
            }
            using (Stream stream = TestUtilities.OpenCompressedFile(x64Dump))
            {
                CheckStartupMemoryReadInternal(stream);
            }
        }

        private static void CheckStartupMemoryReadInternal(Stream stream)
        {
            IAddressSpace sas = new StreamAddressSpace(stream);
            MaxStreamReadHelper readHelper = new MaxStreamReadHelper(sas);

            Minidump minidump = new Minidump(readHelper);

            // We should have read the header of a minidump, so we cannot have read nothing.
            Assert.True(readHelper.Max > 0);

            // We should only read the header and not too far into the dump file, 1k should be plenty.
            Assert.True(readHelper.Max <= 1024);
        }

        private static Minidump GetMinidumpFromStream(Stream stream)
        {
            StreamAddressSpace sas = new(stream);
            return new(sas);
        }
    }
}
