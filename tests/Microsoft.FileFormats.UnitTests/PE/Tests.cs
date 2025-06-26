// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.FileFormats.PE.Tests
{
    public class Tests
    {
        [Fact]
        public void CheckExeIndexingInfo()
        {
            using (Stream s = File.OpenRead("TestBinaries/HelloWorld.exe"))
            {
                StreamAddressSpace fileContent = new(s);
                PEFile pe = new(fileContent);
                Assert.True(pe.IsValid());
                Assert.Equal((uint)0x8000, pe.SizeOfImage);
                Assert.Equal((uint)0x577F5919, pe.Timestamp);
            }
        }

        [Fact]
        public void CheckExePdbInfo()
        {
            using (Stream s = File.OpenRead("TestBinaries/HelloWorld.exe"))
            {
                StreamAddressSpace fileContent = new(s);
                PEFile pe = new(fileContent);

                // There should only be one pdb record entry
                foreach (PEPdbRecord pdb in pe.Pdbs)
                {
                    Assert.Equal(new Guid("99891b3e-d7ae-4c3b-abff-8a2b4a9b0c43"), pdb.Signature);
                    Assert.Equal(1, pdb.Age);
                    Assert.Equal(@"c:\users\noahfalk\documents\visual studio 2015\Projects\HelloWorld\HelloWorld\obj\Debug\HelloWorld.pdb", pdb.Path);
                }
            }
        }

        [Fact]
        public void CheckDllIndexingInfo()
        {
            using (Stream s = File.OpenRead("TestBinaries/System.Diagnostics.StackTrace.dll"))
            {
                StreamAddressSpace fileContent = new(s);
                PEFile pe = new(fileContent);
                Assert.True(pe.IsValid());
                Assert.Equal((uint)0x35a00, pe.SizeOfImage);
                Assert.Equal((uint)0x595cd91b, pe.Timestamp);
            }
        }

        [Fact]
        public void CheckDllPdbInfo()
        {
            using (Stream s = File.OpenRead("TestBinaries/System.Diagnostics.StackTrace.dll"))
            {
                StreamAddressSpace fileContent = new(s);
                PEFile pe = new(fileContent);

                bool first = true;
                foreach (PEPdbRecord pdb in pe.Pdbs)
                {
                    // Skip the first entry (ngen pdb)
                    if (!first)
                    {
                        Assert.Equal(new Guid("8B2E8CF4-4314-4806-982A-B7D904876A50"), pdb.Signature);
                        Assert.Equal(1, pdb.Age);
                        Assert.Equal(@"/root/corefx/bin/obj/Unix.AnyCPU.Release/System.Diagnostics.StackTrace/netcoreapp/System.Diagnostics.StackTrace.pdb", pdb.Path);
                    }
                    first = false;
                }
            }
        }
    }
}
