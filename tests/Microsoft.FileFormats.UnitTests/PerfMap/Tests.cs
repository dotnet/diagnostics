// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.FileFormats.PerfMap;
using Xunit;

namespace Microsoft.FileFormats.PerfMap.Tests
{
    public class Tests
    {
        public static MemoryStream GenerateStreamFromString(string value)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(value ?? ""));
        }

        public const string s_validV1PerfMap = 
@"FFFFFFFF 00 734D59D6DE0E96AA3C77B3E2ED498097
FFFFFFFE 00 1
FFFFFFFD 00 2
FFFFFFFC 00 3
FFFFFFFB 00 1
000115D0 0D Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor()";

        [Fact]
        public void CheckIndexingInfo()
        {
            using (var s = new MemoryStream(Encoding.UTF8.GetBytes(s_validV1PerfMap)))
            {
                PerfMapFile perfMap  = new(s);
                Assert.True(perfMap.IsValid);
                Assert.True(perfMap.Header is not null);
                Assert.True(TestHelpers.TestUtilities.ToHexString(perfMap.Header.Signature) == "734d59d6de0e96aa3c77b3e2ed498097");
                Assert.True(perfMap.Header.Version == 1);
            }
        }

        [Fact]
        public void CheckFields()
        {
            using (var s = new MemoryStream(Encoding.UTF8.GetBytes(s_validV1PerfMap)))
            {
                PerfMapFile perfMap = new(s);
                Assert.True(perfMap.IsValid);
                Assert.True(perfMap.Header is not null);
                Assert.True(TestHelpers.TestUtilities.ToHexString(perfMap.Header.Signature) == "734d59d6de0e96aa3c77b3e2ed498097");
                Assert.True(perfMap.Header.Version == 1);
                Assert.True(perfMap.Header.OperatingSystem == PerfMapFile.PerfMapOSToken.Linux);
                Assert.True(perfMap.Header.Architecture == PerfMapFile.PerfMapArchitectureToken.X64);
                Assert.True(perfMap.Header.Abi == PerfMapFile.PerfMapAbiToken.Default);
            }
        }

        [Fact]
        public void CheckRecords()
        {
            using (var s = new MemoryStream(Encoding.UTF8.GetBytes(s_validV1PerfMap)))
            {
                PerfMapFile perfMap = new(s);
                PerfMapFile.PerfMapRecord record = perfMap.PerfRecords.Single();
                Assert.True(record.Rva == 0x115D0);
                Assert.True(record.Length == 0x0D);
                Assert.True(record.Name == "Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor()");
            }
        }

        public const string s_VNextPerfMapValid = 
@"FFFFFFFF 00 734D59D6DE0E96AA3C77B3E2ED498097
FFFFFFFE 00 99
FFFFFFFD 00 2
FFFFFFFC 00 3
FFFFFFFB 00 1
000115D0 0D Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor()";

        [Fact]
        public void CheckHeaderVNext()
        {
            // Reading the vNext header is valid as long as the signature and fields remain compatible.
            using (var s = new MemoryStream(Encoding.UTF8.GetBytes(s_VNextPerfMapValid)))
            {
                PerfMapFile perfMap = new(s);
                Assert.True(perfMap.IsValid);
                Assert.True(perfMap.Header is not null);
                Assert.True(TestHelpers.TestUtilities.ToHexString(perfMap.Header.Signature) == "734d59d6de0e96aa3c77b3e2ed498097");
                Assert.True(perfMap.Header.Version == 99);
                Assert.True(perfMap.Header.OperatingSystem == PerfMapFile.PerfMapOSToken.Linux);
                Assert.True(perfMap.Header.Architecture == PerfMapFile.PerfMapArchitectureToken.X64);
                Assert.True(perfMap.Header.Abi == PerfMapFile.PerfMapAbiToken.Default);
            }
        }

        [Fact]
        public void CheckRecordsVNextFail()
        {
            // Reading the vNext records is invalid as .
            using (var s = new MemoryStream(Encoding.UTF8.GetBytes(s_VNextPerfMapValid)))
            {
                PerfMapFile perfMap = new(s);
                Assert.True(perfMap.IsValid);
                Assert.True(perfMap.Header is not null);
                Assert.True(perfMap.Header.Version == 99);
                Assert.Throws<NotImplementedException>(perfMap.PerfRecords.First);
            }
        }

        public static IEnumerable<object[]> InvalidSigPerfMaps() =>
            new object[][] {
// Too short
new object[]{@"FFFFFFFF 00 734D59D6DE0E96AA3C77B3E2ED4980
FFFFFFFE 00 1
FFFFFFFD 00 2
FFFFFFFC 00 3
FFFFFFFB 00 1"},
// Not HexString
new object[]{@"FFFFFFFF 00 734D59D6DE0E96AA3C77B3E2ED4980CG
FFFFFFFE 00 1
FFFFFFFD 00 2
FFFFFFFC 00 3
FFFFFFFB 00 1"},
// Too long
new object[]{@"FFFFFFFF 00 734D59D6DE0E96AA3C77B3E2ED49809701
FFFFFFFE 00 1
FFFFFFFD 00 2
FFFFFFFC 00 3
FFFFFFFB 00 1"}};

        [Theory]
        [MemberData(nameof(InvalidSigPerfMaps))]
        public void CheckInvalidSigsFail(string doc)
        {
            using (var s = new MemoryStream(Encoding.UTF8.GetBytes(doc)))
            {
                var perfMap = new PerfMapFile(s);
                Assert.True(!perfMap.IsValid);
            }
        }

        public static IEnumerable<object[]> InvalidHeaders() =>
            new object[][]{
// Wrong token for sig
new object[]{ @"FFFFFFFA 00 734D59D6DE0E96AA3C77B3E2ED4980
FFFFFFFE 00 1
FFFFFFFD 00 2
FFFFFFFC 00 3
FFFFFFFB 00 1" },
// Out of order
new object[]{ @"FFFFFFFF 00 734D59D6DE0E96AA3C77B3E2ED4980CG
FFFFFFFE 00 1
FFFFFFFC 00 3
FFFFFFFD 00 2
FFFFFFFB 00 1"},
// Missing Entry
new object[]{ @"FFFFFFFF 00 734D59D6DE0E96AA3C77B3E2ED498097
FFFFFFFE 00 1
FFFFFFFC 00 3
FFFFFFFB 00 1"},
// Repeated pseudo RVA
new object[]{ @"FFFFFFFF 00 734D59D6DE0E96AA3C77B3E2ED498097
FFFFFFFE 00 1
FFFFFFFD 00 2
FFFFFFFD 00 2
FFFFFFFC 00 3
FFFFFFFB 00 1"},
// Wrong pseudo offset
new object[]{ @"FFFFFFFF 02 734D59D6DE0E96AA3C77B3E2ED498097
FFFFFFFE 00 1
FFFFFFFD 00 2
FFFFFFFC 00 3
FFFFFFFB 00 1"}};

        [Theory]
        [MemberData(nameof(InvalidHeaders))]
        public void CheckInvalidHeadersFail(string doc)
        {
            using (var s = new MemoryStream(Encoding.UTF8.GetBytes(doc)))
            {
                var perfMap = new PerfMapFile(s);
                Assert.True(!perfMap.IsValid);
            }
        }
    }
}
