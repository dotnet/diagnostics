// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using Microsoft.FileFormats.PE;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.SymbolStore.Tests
{
    public class ChecksumValidatorTests
    {
        private const int PdbIdOffset = 40;
        private const int PdbIdSize = 20;
        private const string PdbBodyHex = "DEADBEEFDEADBEEFDEADBEEFDEADBEEF";
        private const string Md5Checksum = "1BFB930837E784E259E446EDE364E061";
        private const string Sha1Checksum = "62D57E5EBE992BE735F9AB46C1545ECF040CCA72";
        private const string Sha256Checksum = "CF90D7BCD182C700DA7E8929B6795F484487E3DDC78277B5246E3F38537FA065";
        private const string Sha384Checksum = "EF81929A2474618876813AABAD30491C4B749AB0CB5AFBC6959E9D68694E65114DFC2A9519EE0E5941DD15ADB560EA9A";
        private const string Sha512Checksum = "E93DE7623C5EBE53BEAF71206D23F6BEAB2A7A4D8225255BC319300ECE56DF4C4ADE05CBCDDB8863FAD62A6C5291BAF9CA88362C6527AB4387E869D8AF8C353E";
        private readonly ITracer _tracer;

        public ChecksumValidatorTests(ITestOutputHelper output)
        {
            _tracer = new Tracer(output);
        }

        [Theory]
        [InlineData("MD5", Md5Checksum)]
        [InlineData("SHA1", Sha1Checksum)]
        [InlineData("SHA256", Sha256Checksum)]
        [InlineData("SHA384", Sha384Checksum)]
        [InlineData("SHA512", Sha512Checksum)]
        public void ValidateAcceptsSupportedAlgorithm(string algorithmName, string checksumHex)
        {
            using MemoryStream pdbStream = CreatePdbStream();
            byte[] originalBytes = pdbStream.ToArray();
            PdbChecksum checksum = new(algorithmName, Convert.FromHexString(checksumHex));

            ChecksumValidator.Validate(_tracer, pdbStream, new[] { checksum });

            Assert.Equal(0, pdbStream.Position);
            Assert.Equal(originalBytes, pdbStream.ToArray());
        }

        [Theory]
        [InlineData("Unknown")]
        [InlineData("sha256")]
        [InlineData("SHA-256")]
        [InlineData("System.Security.Cryptography.SHA256")]
        [InlineData("System.Security.Cryptography.HashAlgorithm")]
        [InlineData("RIPEMD160")]
        public void ValidateRejectsUnknownAlgorithm(string algorithmName)
        {
            using MemoryStream pdbStream = CreatePdbStream();
            PdbChecksum checksum = new(algorithmName, new byte[32]);

            InvalidChecksumException exception = Assert.Throws<InvalidChecksumException>(
                () => ChecksumValidator.Validate(_tracer, pdbStream, new[] { checksum }));

            Assert.Equal($"Unknown hash algorithm: {algorithmName}", exception.Message);
        }

        [Theory]
        [InlineData("MD5", 16)]
        [InlineData("SHA1", 20)]
        [InlineData("SHA256", 32)]
        [InlineData("SHA384", 48)]
        [InlineData("SHA512", 64)]
        public void ValidateReportsMismatchForKnownAlgorithm(string algorithmName, int checksumSize)
        {
            using MemoryStream pdbStream = CreatePdbStream();
            PdbChecksum[] checksums =
            {
                new("Unknown", new byte[32]),
                new(algorithmName, new byte[checksumSize])
            };

            InvalidChecksumException exception = Assert.Throws<InvalidChecksumException>(
                () => ChecksumValidator.Validate(_tracer, pdbStream, checksums));

            Assert.Equal("PDB checksum mismatch", exception.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ValidateAcceptsAnyMatchingChecksum(bool matchingChecksumFirst)
        {
            using MemoryStream pdbStream = CreatePdbStream();
            PdbChecksum[] checksums =
            {
                new("Unknown", new byte[32]),
                new("SHA256", new byte[32]),
                new("SHA1", Convert.FromHexString(Sha1Checksum))
            };
            if (matchingChecksumFirst)
            {
                Array.Reverse(checksums);
            }

            ChecksumValidator.Validate(_tracer, pdbStream, checksums);

            Assert.Equal(0, pdbStream.Position);
        }

        [Fact]
        public void ValidateReportsAllUnknownAlgorithms()
        {
            using MemoryStream pdbStream = CreatePdbStream();
            PdbChecksum[] checksums =
            {
                new("UnknownOne", new byte[32]),
                new("UnknownTwo", new byte[32])
            };

            InvalidChecksumException exception = Assert.Throws<InvalidChecksumException>(
                () => ChecksumValidator.Validate(_tracer, pdbStream, checksums));

            Assert.Equal("Unknown hash algorithm: UnknownOne UnknownTwo", exception.Message);
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void ValidateRejectsMissingAlgorithmName(string algorithmName, bool includeMatchingChecksum)
        {
            using MemoryStream pdbStream = CreatePdbStream();
            PdbChecksum checksum = new(algorithmName, new byte[32]);
            PdbChecksum[] checksums = includeMatchingChecksum
                ? new[] { checksum, new PdbChecksum("SHA256", Convert.FromHexString(Sha256Checksum)) }
                : new[] { checksum };

            Assert.ThrowsAny<ArgumentException>(
                () => ChecksumValidator.Validate(_tracer, pdbStream, checksums));
        }

        private static MemoryStream CreatePdbStream()
        {
            byte[] pdbBody = Convert.FromHexString(PdbBodyHex);
            MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(new byte[] { 0x42, 0x53, 0x4A, 0x42 }); // Metadata signature ("BSJB")
                writer.Write((ushort)1); // Major version
                writer.Write((ushort)0); // Minor version
                writer.Write(0u); // Reserved
                writer.Write(4u); // Version string size
                writer.Write(new byte[] { (byte)'v', (byte)'1', 0, 0 }); // Padded version string
                writer.Write((ushort)0); // Storage flags and padding
                writer.Write((ushort)1); // Stream count
                writer.Write((uint)PdbIdOffset); // #Pdb stream offset
                writer.Write((uint)(PdbIdSize + pdbBody.Length)); // #Pdb stream size
                writer.Write(Encoding.UTF8.GetBytes("#Pdb\0")); // Null-terminated stream name

                stream.Position = PdbIdOffset;
                // The validator excludes the 20-byte PDB ID from the checksum and restores it after validation.
                for (int i = 0; i < PdbIdSize; i++)
                {
                    writer.Write((byte)(i + 1));
                }

                writer.Write(pdbBody);
            }
            stream.Position = 0;
            return stream;
        }
    }
}
