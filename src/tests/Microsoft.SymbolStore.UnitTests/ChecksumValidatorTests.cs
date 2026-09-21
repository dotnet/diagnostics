// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Security.Cryptography;
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
        private readonly ITracer _tracer;

        public ChecksumValidatorTests(ITestOutputHelper output)
        {
            _tracer = new Tracer(output);
        }

        [Theory]
        [InlineData("MD5")]
        [InlineData("SHA1")]
        [InlineData("SHA256")]
        [InlineData("SHA384")]
        [InlineData("SHA512")]
        public void ValidateAcceptsSupportedAlgorithm(string algorithmName)
        {
            using MemoryStream pdbStream = CreatePdbStream();
            byte[] originalBytes = pdbStream.ToArray();
            PdbChecksum checksum = new(algorithmName, ComputeChecksum(algorithmName, pdbStream));

            ChecksumValidator.Validate(_tracer, pdbStream, new[] { checksum });

            Assert.Equal(0, pdbStream.Position);
            Assert.Equal(originalBytes, pdbStream.ToArray());
        }

        [Fact]
        public void ValidateRejectsUnknownAlgorithm()
        {
            using MemoryStream pdbStream = CreatePdbStream();
            PdbChecksum checksum = new("Unknown", new byte[32]);

            InvalidChecksumException exception = Assert.Throws<InvalidChecksumException>(
                () => ChecksumValidator.Validate(_tracer, pdbStream, new[] { checksum }));

            Assert.Equal("Unknown hash algorithm: Unknown", exception.Message);
        }

        [Theory]
        [InlineData("MD5")]
        [InlineData("SHA1")]
        [InlineData("SHA256")]
        [InlineData("SHA384")]
        [InlineData("SHA512")]
        public void ValidateReportsMismatchForKnownAlgorithm(string algorithmName)
        {
            using MemoryStream pdbStream = CreatePdbStream();
            PdbChecksum[] checksums =
            {
                new("Unknown", new byte[32]),
                new(algorithmName, Array.Empty<byte>())
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
                new("SHA1", ComputeChecksum("SHA1", pdbStream))
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

        [Fact]
        public void ValidateHandlesPlatformDependentAlgorithm()
        {
            using MemoryStream pdbStream = CreatePdbStream();
            PdbChecksum checksum = new("SHA3-256", SHA3_256.IsSupported
                ? ComputeChecksum("SHA3-256", pdbStream)
                : new byte[32]);

            if (SHA3_256.IsSupported)
            {
                ChecksumValidator.Validate(_tracer, pdbStream, new[] { checksum });
            }
            else
            {
                InvalidChecksumException exception = Assert.Throws<InvalidChecksumException>(
                    () => ChecksumValidator.Validate(_tracer, pdbStream, new[] { checksum }));
                Assert.Equal("Unknown hash algorithm: SHA3-256", exception.Message);

                pdbStream.Position = 0;
                PdbChecksum fallback = new("SHA256", ComputeChecksum("SHA256", pdbStream));
                ChecksumValidator.Validate(_tracer, pdbStream, new[] { checksum, fallback });
            }

            Assert.Equal(0, pdbStream.Position);
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
                ? new[] { checksum, new PdbChecksum("SHA256", ComputeChecksum("SHA256", pdbStream)) }
                : new[] { checksum };

            Assert.ThrowsAny<ArgumentException>(
                () => ChecksumValidator.Validate(_tracer, pdbStream, checksums));
        }

        private static MemoryStream CreatePdbStream()
        {
            MemoryStream stream = new(new byte[PdbIdOffset + PdbIdSize]);
            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(0x424A5342u);
                writer.Write((ushort)1);
                writer.Write((ushort)0);
                writer.Write(0u);
                writer.Write(4u);
                writer.Write(new byte[] { (byte)'v', (byte)'1', 0, 0 });
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((uint)PdbIdOffset);
                writer.Write((uint)PdbIdSize);
                writer.Write(Encoding.UTF8.GetBytes("#Pdb\0"));

                stream.Position = PdbIdOffset;
                for (int i = 0; i < PdbIdSize; i++)
                {
                    writer.Write((byte)(i + 1));
                }
            }
            stream.Position = 0;
            return stream;
        }

        private static byte[] ComputeChecksum(string algorithmName, MemoryStream pdbStream)
        {
            byte[] bytes = pdbStream.ToArray();
            for (int i = 0; i < PdbIdSize; i++)
            {
                bytes[PdbIdOffset + i] = 0;
            }

            using HashAlgorithm algorithm = algorithmName switch
            {
                "MD5" => MD5.Create(),
                "SHA1" => SHA1.Create(),
                "SHA256" => SHA256.Create(),
                "SHA384" => SHA384.Create(),
                "SHA512" => SHA512.Create(),
                "SHA3-256" => SHA3_256.Create(),
                _ => throw new InvalidDataException()
            };
            return algorithm.ComputeHash(bytes);
        }
    }
}
