// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Microsoft.FileFormats.PerfMap
{
    public sealed class PerfMapFile
    {
        // See format of the perfmap file at https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/r2r-perfmap-format.md
        private const int PerfMapV1SigLength = 16;

        private const int PerfMapV1HeaderRecordCount = 5;

        public const int MaxKnownPerfMapVersion = 1;

        private const int HeaderRecordPseudoLength = 0;

        private enum PerfMapPseudoRVAToken : uint
        {
            OutputSignature = 0xFFFFFFFF,
            FormatVersion = 0xFFFFFFFE,
            TargetOS = 0xFFFFFFFD,
            TargetArchitecture = 0xFFFFFFFC,
            TargetABI = 0xFFFFFFFB,
        }

        public enum PerfMapArchitectureToken : uint
        {
            Unknown = 0,
            ARM = 1,
            ARM64 = 2,
            X64 = 3,
            X86 = 4,
        }

        public enum PerfMapOSToken : uint
        {
            Unknown = 0,
            Windows = 1,
            Linux = 2,
            OSX = 3,
            FreeBSD = 4,
            NetBSD = 5,
            SunOS = 6,
        }

        public enum PerfMapAbiToken : uint
        {
            Unknown = 0,
            Default = 1,
            Armel = 2,
        }

        private readonly Stream _stream;
        private readonly Lazy<PerfMapHeader> _header;

        public PerfMapHeader Header { get => _header.Value; }

        public bool IsValid => Header is not null;

        public IEnumerable<PerfMapRecord> PerfRecords
        {
            get
            {
                ThrowIfInvalid();

                if (Header.Version > MaxKnownPerfMapVersion)
                {
                    throw new NotImplementedException($"Format version {Header.Version} unknown. Max known format is {MaxKnownPerfMapVersion}");
                }
                using StreamReader reader = new(_stream, Encoding.UTF8, false, 1024, leaveOpen: true);

                // Skip over the header.
                // For now this is V1, the length will need to be a lookup on the version.
                for (int i = 0; i < PerfMapV1HeaderRecordCount; ++i)
                {
                    _ = reader.ReadLine();
                }
                while (true)
                {
                    PerfMapFile.PerfMapRecord cur = ReadRecord(reader);
                    if (cur is null)
                    {
                        yield break;
                    }
                    yield return cur;
                }
            }
        }

        private void ThrowIfInvalid()
        {
            if (!IsValid)
            {
                throw new BadInputFormatException("The PerfMap is not valid");
            }
        }

        public PerfMapFile(Stream stream)
        {
            System.Diagnostics.Debug.Assert(stream.CanSeek);
            _stream = stream;
            _header = new Lazy<PerfMapHeader>(ReadHeader, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private PerfMapHeader ReadHeader()
        {
            static bool IsValidHeaderRecord(PerfMapPseudoRVAToken expectedToken, PerfMapRecord record)
                => record is not null && (uint)expectedToken == record.Rva
                    && record.Length == HeaderRecordPseudoLength;

            long prevPosition = _stream.Position;
            try
            {
                _stream.Position = 0;
                // Headers don't need much of a buffer.
                using StreamReader reader = new(_stream, Encoding.UTF8, false, 256, leaveOpen: true);

                PerfMapRecord sigRecord = ReadRecord(reader);
                if (!IsValidHeaderRecord(PerfMapPseudoRVAToken.OutputSignature, sigRecord) ||
                    !Helpers.TryConvertHexStringToBytes(sigRecord.Name, out byte[] sigBytes) ||
                     sigBytes?.Length != PerfMapV1SigLength)
                {
                    return null;
                }
                PerfMapRecord versionRecord = ReadRecord(reader);
                if (!IsValidHeaderRecord(PerfMapPseudoRVAToken.FormatVersion, versionRecord) ||
                    !uint.TryParse(versionRecord.Name, out uint version))
                {
                    return null;
                }
                PerfMapRecord osRecord = ReadRecord(reader);
                if (!IsValidHeaderRecord(PerfMapPseudoRVAToken.TargetOS, osRecord) ||
                    !uint.TryParse(osRecord.Name, out uint os))
                {
                    return null;
                }
                PerfMapRecord archRecord = ReadRecord(reader);
                if (!IsValidHeaderRecord(PerfMapPseudoRVAToken.TargetArchitecture, archRecord) ||
                    !uint.TryParse(archRecord.Name, out uint arch))
                {
                    return null;
                }
                PerfMapRecord abiRecord = ReadRecord(reader);
                if (!IsValidHeaderRecord(PerfMapPseudoRVAToken.TargetABI, abiRecord) ||
                    !uint.TryParse(abiRecord.Name, out uint abi))
                {
                    return null;
                }
                return new PerfMapHeader(sigBytes, version, os, arch, abi);
                // Append as necessary as revisions get added here.
                // We don't return null on a higher versioned heder than the max known as they are backwards compatible and they are not necessary for indexing.
            }
            catch (Exception ex) when (ex is BadInputFormatException || ex is EndOfStreamException)
            {

            }
            finally
            {
                _stream.Position = prevPosition;
            }
            return null;
        }

        private static PerfMapRecord ReadRecord(StreamReader reader)
        {
            string[] segments = reader.ReadLine()?.Split();

            if (segments is null)
            {
                return null;
            }
            if (segments.Length != 3)
            {
                throw new BadInputFormatException("Entry on perfmap record doesn't have 3 segments.");
            }
            if (!uint.TryParse(segments[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rva))
            {
                throw new BadInputFormatException("Record's RVA is not a valid hex unsigned int.");
            }
            if (!ushort.TryParse(segments[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort length))
            {
                throw new BadInputFormatException("Record's Length is not a valid hex unsigned int.");
            }
            return new PerfMapRecord(rva, length, segments[2]);
        }

        public sealed class PerfMapRecord
        {
            public PerfMapRecord(uint rva, ushort length, string entryName)
            {
                Rva = rva;
                Length = length;
                Name = entryName;
            }

            public uint Rva { get; }
            public ushort Length { get; }
            public string Name { get; }
        }

        public sealed class PerfMapHeader
        {
            public PerfMapHeader(byte[] signature, uint version, uint operatingSystem, uint architecture, uint abi)
            {
                Signature = signature;
                Version = version;
                OperatingSystem = (PerfMapOSToken) operatingSystem;
                Architecture = (PerfMapArchitectureToken) architecture;
                Abi = (PerfMapAbiToken) abi;
            }

            public byte[] Signature { get; }
            public uint Version { get; }
            public PerfMapOSToken OperatingSystem { get; }
            public PerfMapArchitectureToken Architecture { get; }
            public PerfMapAbiToken Abi { get; }
        }
    }
}
