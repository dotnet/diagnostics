// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.FileFormats;
using Microsoft.FileFormats.PDB;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore
{
    internal sealed class ChecksumValidator
    {
        private const string pdbStreamName = "#Pdb";
        private const uint pdbIdSize = 20;

        internal static void Validate(ITracer tracer, Stream pdbStream, IEnumerable<PdbChecksum> pdbChecksums)
        {
            // A portable PDB checksum is computed over the metadata image with the embedded
            // PDB id zeroed out, so it can be fully recomputed and validated here.
            //
            // Windows PDBs (MSF container) and PDZ files (MSFZ container) use a completely
            // different on-disk format that this code cannot recompute. So for those, we
            // check with PDBFile class and accept it.
            //
            // This happens for ngen or ReadyToRun images.
            if (IsPortablePdb(pdbStream))
            {
                ValidatePortablePdb(tracer, pdbStream, pdbChecksums);
            }
            else
            {
                ValidateWindowsPdb(tracer, pdbStream);
            }
        }

        /// <summary>
        /// Returns true if the stream is a portable PDB (an ECMA-335 metadata image, which
        /// starts with the "BSJB" signature).
        /// </summary>
        private static bool IsPortablePdb(Stream pdbStream)
        {
            pdbStream.Position = 0;
            byte[] signature = new byte[4];
            int read = pdbStream.Read(signature, 0, signature.Length);
            pdbStream.Position = 0;
            return read == signature.Length &&
                   signature[0] == 0x42 && // 'B'
                   signature[1] == 0x53 && // 'S'
                   signature[2] == 0x4A && // 'J'
                   signature[3] == 0x42;   // 'B'
        }

        /// <summary>
        /// Structurally validates a Windows PDB (MSF) or PDZ (MSFZ) download and accepts it.
        /// A byte-level re-validation is not performed.
        /// </summary>
        private static void ValidateWindowsPdb(ITracer tracer, Stream pdbStream)
        {
            const string checksumExceptionMessage = "The downloaded file is neither a portable PDB nor a valid Windows PDB (MSF/MSFZ) container";
            pdbStream.Position = 0;
            try
            {
                using (PDBFile pdbFile = new(new StreamAddressSpace(pdbStream)))
                {
                    if (!pdbFile.IsValid())
                    {
                        throw new InvalidChecksumException(checksumExceptionMessage);
                    }
                    tracer.Information($"Accepting Windows PDB ({pdbFile.ContainerKind}); No checksum validation is available for this file format");
                }
            }
            catch (Exception)  // The PDBFile constructor or IsValid method can throw an Exception exception
            {
                // Note: the InvalidChecksumException constructor does not accept an inner exception
                throw new InvalidChecksumException(checksumExceptionMessage);
            }
            pdbStream.Position = 0;
        }

        private static void ValidatePortablePdb(ITracer tracer, Stream pdbStream, IEnumerable<PdbChecksum> pdbChecksums)
        {
            uint offset = 0;

            pdbStream.Position = 0;
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
