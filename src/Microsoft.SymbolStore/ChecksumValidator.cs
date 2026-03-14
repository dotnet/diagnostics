// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore
{
    internal sealed class ChecksumValidator
    {
        private const string pdbStreamName = "#Pdb";
        private const uint pdbIdSize = 20;

        internal static void Validate(ITracer tracer, Stream pdbStream, IEnumerable<PdbChecksum> pdbChecksums)
        {
            uint offset = 0;

            byte[] bytes = new byte[pdbStream.Length];
            byte[] pdbId = new byte[pdbIdSize];
            if (pdbStream.Read(bytes, offset: 0, count: bytes.Length) != bytes.Length)
            {
                throw new InvalidChecksumException("Unexpected stream length");
            }

            try
            {
                offset = GetPdbStreamOffset(pdbStream);
            }
            catch (Exception ex)
            {
                tracer.Error(ex.Message);
                throw;
            }

            // Make a copy of the pdb Id
            Array.Copy(bytes, offset, pdbId, 0, pdbIdSize);

            // Zero out the pdb Id
            for (int i = 0; i < pdbIdSize; i++)
            {
                bytes[i + offset] = 0;
            }

            bool algorithmNameKnown = false;
            foreach (PdbChecksum checksum in pdbChecksums)
            {
                tracer.Information($"Testing checksum: {checksum}");

                HashAlgorithm algorithm = HashAlgorithm.Create(checksum.AlgorithmName);
                if (algorithm != null)
                {
                    algorithmNameKnown = true;
                    byte[] hash = algorithm.ComputeHash(bytes);
                    if (hash.SequenceEqual(checksum.Checksum))
                    {
                        // If any of the checksums are OK, we're good
                        tracer.Information($"Found checksum match {checksum}");
                        // Restore the pdb Id
                        Array.Copy(pdbId, 0, bytes, offset, pdbIdSize);
                        // Restore the steam position
                        pdbStream.Seek(0, SeekOrigin.Begin);

                        return;
                    }
                }
            }

            if (!algorithmNameKnown)
            {
                string algorithmNames = string.Join(" ", pdbChecksums.Select(c => c.AlgorithmName));
                throw new InvalidChecksumException($"Unknown hash algorithm: {algorithmNames}");
            }

            throw new InvalidChecksumException("PDB checksum mismatch");
        }

        private static uint GetPdbStreamOffset(Stream pdbStream)
        {
            pdbStream.Position = 0;
            using (BinaryReader reader = new(pdbStream, Encoding.UTF8, leaveOpen: true))
            {
                pdbStream.Seek(4 + // Signature
                               2 + // Version Major
                               2 + // Version Minor
                               4,  // Reserved)
                               SeekOrigin.Begin);

                // skip the version string
                uint versionStringSize = reader.ReadUInt32();

                pdbStream.Seek(versionStringSize, SeekOrigin.Current);

                // storage header
                pdbStream.Seek(2, SeekOrigin.Current);

                // read the stream headers
                ushort streamCount = reader.ReadUInt16();
                uint streamOffset;
                string streamName;

                for (int i = 0; i < streamCount; i++)
                {
                    streamOffset = reader.ReadUInt32();
                    // stream size
                    pdbStream.Seek(4, SeekOrigin.Current);
                    streamName = reader.ReadNullTerminatedString();

                    if (streamName == pdbStreamName)
                    {
                        // We found it!
                        return streamOffset;
                    }

                    // streams headers are on a four byte alignment
                    if (pdbStream.Position % 4 != 0)
                    {
                        pdbStream.Seek(4 - pdbStream.Position % 4, SeekOrigin.Current);
                    }
                }
            }

            throw new ArgumentException("We have a file with a metadata pdb signature but no pdb stream");
        }
    }

    public static class BinaryReaderExtensions
    {
        public static string ReadNullTerminatedString(this BinaryReader stream)
        {
            StringBuilder builder = new();
            char ch;
            while ((ch = stream.ReadChar()) != 0)
            {
                builder.Append(ch);
            }
            return builder.ToString();
        }
    }
}
