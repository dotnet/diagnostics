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
        public void CheckIndexingInfo()
        {
            using (Stream s = File.OpenRead("TestBinaries/HelloWorld.pdb"))
            {
                StreamAddressSpace fileContent = new StreamAddressSpace(s);
                PDBFile pdb = new PDBFile(fileContent);
                Assert.True(pdb.Header.IsMagicValid.Check());
                Assert.True(pdb.IsValid());
                Assert.Equal((uint)1, pdb.Age);
                Assert.Equal(Guid.Parse("99891B3E-D7AE-4C3B-ABFF-8A2B4A9B0C43"), pdb.Signature);
            }
        }
    }
}
