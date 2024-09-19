// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.FileFormats.PDB.Tests
{
    public class Tests
    {
        [Fact]
        public void CheckIndexingInfoPdb()
        {
            using (Stream s = File.OpenRead("TestBinaries/HelloWorld.pdb"))
            {
                StreamAddressSpace fileContent = new StreamAddressSpace(s);
                PDBFile pdb = PDBFile.Open(fileContent);
                Assert.Equal((uint)1, pdb.Age);
                Assert.Equal(Guid.Parse("99891B3E-D7AE-4C3B-ABFF-8A2B4A9B0C43"), pdb.Signature);

                Assert.Equal(PDBContainerKind.MSF, pdb.ContainerKind);
                Assert.Equal("msf", pdb.ContainerKindSpecString);

                // Also read the PDBI stream directly, using the downlevel API.
#pragma warning disable CS0618 // Type or member is obsolete
                var stream1 = pdb.Streams[1];
                Assert.Equal<ulong>(226ul, stream1.Length);
                byte[] buffer = new byte[226];
                Assert.Equal<uint>(226u, stream1.Read(0, buffer, 0, 226));
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }

        [Fact]
        public void CheckIndexingInfoPdz()
        {
            using (Stream s = File.OpenRead("TestBinaries/HelloWorld.pdz"))
            {
                StreamAddressSpace fileContent = new StreamAddressSpace(s);
                PDBFile pdb = PDBFile.Open(fileContent);
                Assert.Equal((uint)1, pdb.Age);
                Assert.Equal(Guid.Parse("99891B3E-D7AE-4C3B-ABFF-8A2B4A9B0C43"), pdb.Signature);

                Assert.Equal(PDBContainerKind.MSFZ, pdb.ContainerKind);
                Assert.Equal("msfz0", pdb.ContainerKindSpecString);

                // Also read the PDBI stream directly, using the downlevel API.
#pragma warning disable CS0618 // Type or member is obsolete
                var stream1 = pdb.Streams[1];
                Assert.Equal<ulong>(226ul, stream1.Length);
                byte[] buffer = new byte[226];
                Assert.Equal<uint>(226u, stream1.Read(0, buffer, 0, 226));
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }
    }
}
