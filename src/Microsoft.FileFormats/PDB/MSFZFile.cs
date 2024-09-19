// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.FileFormats.PDB
{
    /// <summary>
    /// This class can read data from PDB files that use the MSFZ container format.
    /// </summary>
    internal sealed class MSFZFile : IMSFFile, IDisposable
    {
        /// <summary>
        /// Provides access to the underlying MSFZ file.
        /// </summary>
        private readonly Reader _reader;

        /// <summary>
        /// The number of streams.
        /// </summary>
        private readonly uint _numStreams;

        /// <summary>
        /// The encoded Stream Directory.  See the MSFZ specification for details on the encoding.
        /// This is stored as an array of uint rather than an array of byte because the underlying
        /// encoding stores uint32 values; there are no values larger than uint32 and no values
        /// smaller.
        /// </summary>
        private readonly uint[] _streamDir;

        /// <summary>
        /// _streamDirStarts[s] gives the index into _streamDir where the fragments for stream s begin.
        /// _streamDirStarts.Length == _numStreams.
        /// </summary>
        private readonly uint[] _streamDirStarts;

        /// <summary>
        /// The value of the "version" field from the MSFZ header.
        /// </summary>
        private readonly ulong _msfzVersion;

        private MSFZFile(Reader reader, uint numStreams, uint[] streamDir, uint[] streamDirStarts, ulong msfzVersion)
        {
            Debug.Assert(numStreams == streamDirStarts.Length);
            this._numStreams = numStreams;
            this._reader = reader;
            this._streamDir = streamDir;
            this._streamDirStarts = streamDirStarts;
            this._msfzVersion = msfzVersion;
        }

        public uint NumStreams
        {
            get { return _numStreams; }
        }

        public Reader GetStream(uint stream)
        {
            if (stream == 0 || stream >= _numStreams)
            {
                throw new ArgumentException("Invalid stream index");
            }

            uint streamSize = GetStreamSize(stream);
            return new Reader(new MsfzStream(this, stream, streamSize));
        }

        /// <summary>
        /// Returns the size of the stream. The size is computed by iterating the fragments that
        /// make up the stream and computing the sum of their sizes.
        /// </summary>
        /// <param name="stream">The stream index. The caller must validate this value against
        /// NumStreams.</param>
        /// <returns>The total size in bytes of the stream.</returns>
        internal uint GetStreamSize(uint stream)
        {
            uint streamSize = 0;

            uint pos = _streamDirStarts[stream];
            while (pos < _streamDir.Length)
            {
                uint fragmentSize = _streamDir[pos];

                // Nil streams do not have any fragments and are zero-length.
                if (fragmentSize == MsfzConstants.NilFragmentSize)
                {
                    return 0;
                }

                // The fragment list (for a given stream) is terminated by a 0 value.
                if (fragmentSize == 0)
                {
                    break;
                }

                streamSize += fragmentSize;

                // Step over this fragment record.
                pos += MsfzConstants.UInt32PerFragmentRecord;
            }

            return streamSize;
        }

        // Can return null, on failure.
        internal static MSFZFile Open(IAddressSpace dataSource)
        {
            Reader reader = new(dataSource);

            ulong pos = 0;

            MSFZFileHeader fileHeader = reader.Read<MSFZFileHeader>(ref pos);

            if (!fileHeader.Signature.SequenceEqual(MSFZFileHeader.ExpectedSignature))
            {
                // Wrong signature
                return null;
            }

            ulong headerVersion = fileHeader.Version;
            uint numStreams = fileHeader.NumStreams;
            ulong streamDirOffset = fileHeader.StreamDirOffset;
            uint streamDirCompression = fileHeader.StreamDirCompression;
            uint streamDirSizeCompressed = fileHeader.StreamDirSizeCompressed;
            uint streamDirSizeUncompressed = fileHeader.StreamDirSizeUncompressed;

            // Validate the MSFZ file header version. We keep track of the version in a variable,
            // even though the only version that is actually supported is V0. This is to minimize
            // code changes in future versions of this code that would parse V1, V2, etc.
            if (headerVersion != MSFZFileHeader.VersionV0)
            {
                // Wrong version
                return null;
            }

            if (streamDirCompression != MSFZConstants.COMPRESSION_NONE
                || streamDirSizeCompressed != streamDirSizeUncompressed)
            {
                // Stream directory compression is not supported
                return null;
            }

            if (streamDirSizeUncompressed % 4 != 0)
            {
                // Stream directory length should be a multiple of uint32 size.
                return null;
            }

            // Read the contents of the stream dir, as a sequence of bytes.
            byte[] streamDirBytes = reader.Read(streamDirOffset, streamDirSizeUncompressed);

            uint streamDirEncodedSize = streamDirSizeUncompressed;
            uint[] streamDirEncoded = new uint[streamDirEncodedSize / 4];
            for (int i = 0; i < streamDirEncoded.Length; ++i)
            {
                streamDirEncoded[i] = BitConverter.ToUInt32(streamDirBytes, i * 4);
            }

            uint[] streamStarts = FindStreamDirStarts(numStreams, streamDirEncoded);

            // We do not read the Chunk Table because this implementation does not support
            // compression. Since the Chunk Table describes compressed chunks, we will never use it.

            return new MSFZFile(reader, numStreams, streamDirEncoded, streamStarts, headerVersion);
        }

        /// <summary>
        /// Scans through the encoded form of the Stream Directory (in uint32 form, not byte form)
        /// and builds a table of the starting locations of the fragments of each stream.
        /// </summary>
        private static uint[] FindStreamDirStarts(uint numStreams, uint[] streamDir)
        {
            uint pos = 0; // index within streamDir where the fragments for the current stream begin

            uint[] starts = new uint[numStreams];

            for (uint stream = 0; stream < numStreams; ++stream)
            {
                starts[stream] = pos;

                if (pos >= streamDir.Length)
                {
                    throw new Exception("stream directory is too short to be valid");
                }

                uint fragmentSize = streamDir[pos];
                if (fragmentSize == MsfzConstants.NilFragmentSize)
                {
                    // It's a nil stream.
                    ++pos;
                    continue;
                }

                // Read all the fragments of this stream.  There may be no fragments at all.
                while (fragmentSize != 0)
                {
                    // There should be at least 3 more words.  The next 2 words form the fragment location
                    // of the current fragment.  The next word is either the length of the next fragment
                    // or is 0, indicating the end of the fragment list.
                    if (pos + MsfzConstants.UInt32PerFragmentRecord >= streamDir.Length)
                    {
                        throw new Exception("MSFZ stream directory is too short to be valid");
                    }

                    // Advance our pointer and read the size of the next fragment.
                    pos += MsfzConstants.UInt32PerFragmentRecord;
                    fragmentSize = streamDir[pos];
                }

                // This steps over the 0 at the end of the fragment list.
                ++pos;
            }

            return starts;
        }

        /// <summary>
        /// Reads data from a stream.
        /// </summary>
        /// <param name="stream">The stream index</param>
        /// <param name="position">The byte offset within the stream of the data.</param>
        /// <param name="buffer">The output buffer.</param>
        /// <param name="bufferOffset">The location within the output buffer to write the data.</param>
        /// <param name="count">The number of bytes to transfer.</param>
        /// <returns>The number of bytes actually transferred.</returns>
        /// <exception cref="NotSupportedException">Thrown if the data covers a compressed fragment.</exception>
        /// <exception cref="Exception"></exception>
        internal uint ReadStream(uint stream, ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            // Find the fragments for this stream.
            uint fragmentIndex = _streamDirStarts[stream];
            uint totalBytesTransferred = 0;

            while (count != 0)
            {
                uint fragmentSize = _streamDir[fragmentIndex];
                if (fragmentSize == MsfzConstants.NilFragmentSize || fragmentSize == 0)
                {
                    break;
                }

                // We can safely index into _streamDir because we have already validated its contents,
                // when we first opened the MSFZ file.

                MsfzFragmentLocation fragmentLocation;
                fragmentLocation.Low = _streamDir[fragmentIndex + 1];
                fragmentLocation.High = _streamDir[fragmentIndex + 2];
                fragmentIndex += MsfzConstants.UInt32PerFragmentRecord;

                // If the position is beyond the range of bytes covered by this fragment, then skip
                // this fragment and adjust our read position.
                if (position >= fragmentSize)
                {
                    position -= fragmentSize;
                    continue;
                }

                // We found the fragment that we are going to read from.
                uint transferSize = Math.Min(count, (uint)(fragmentSize - position));

                // Is this fragment compressed or uncompressed?
                if (fragmentLocation.IsCompressedChunk)
                {
                    // Compressed fragments are not supported by this implementation.
                    throw new NotSupportedException($"This implementation does not support decompression of PDZ (compressed PDB) files. "
                        + $"It can only read data from uncompressed streams within PDZ files. (stream {stream}, position {position})");
                }

                ulong fileOffset = fragmentLocation.UncompressedFileOffset + position;

                uint fileBytesTransferred = _reader.Read(fileOffset, buffer, bufferOffset, transferSize);
                if (fileBytesTransferred != transferSize)
                {
                    // We expect the entire read to be satisfied.
                    throw new Exception("Internal error in MSFZ reader. The underlying file reader did not read enough data from the file.");
                }

                count -= transferSize;
                bufferOffset += transferSize;
                position += transferSize;
                totalBytesTransferred += transferSize;
            }

            return totalBytesTransferred;
        }

        public PDBContainerKind ContainerKind
        {
            get { return PDBContainerKind.MSFZ; }
        }

        public string ContainerKindSpecString
        {
            get { return $"msfz{_msfzVersion}"; }
        }

        public void Dispose()
        {
            if (_reader.DataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// Describes the MSFZ File Header on-disk data structure.
    /// </summary>
    internal sealed class MSFZFileHeader : TStruct
    {
        // 00000000 :  4d 69 63 72 6f 73 6f 66 74 20 4d 53 46 5a 20 43 : Microsoft MSFZ C
        // 00000010 :  6f 6e 74 61 69 6e 65 72 0d 0a 1a 41 4c 44 00 00 : ontainer...ALD..

        internal static readonly byte[] ExpectedSignature =
        {
            0x4d, 0x69, 0x63, 0x72, 0x6f, 0x73, 0x6f, 0x66, 0x74, 0x20, 0x4d, 0x53, 0x46, 0x5a, 0x20, 0x43, // : Microsoft MSFZ C
            0x6f, 0x6e, 0x74, 0x61, 0x69, 0x6e, 0x65, 0x72, 0x0d, 0x0a, 0x1a, 0x41, 0x4c, 0x44, 0x00, 0x00, // : ontainer...ALD..
        };

        internal const ulong VersionV0 = 0;

        internal const int SizeOf = 80;

#pragma warning disable 0649 // These fields are assigned via Reflection, so the C# compiler thinks they are never assigned.

        /// <summary>Identifies this as an MSFZ file.</summary>
        [ArraySize(32)]
        internal byte[] Signature;
        /// <summary>specifies the version number of the MSFZ file format</summary>
        internal ulong Version;
        /// <summary>file offset of the stream directory</summary>
        internal ulong StreamDirOffset;
        /// <summary>file offset of the chunk table</summary>
        internal ulong ChunkTableOffset;
        /// <summary>the number of streams stored within this MSFZ file</summary>
        internal uint NumStreams;
        /// <summary>compression algorithm used for the stream directory</summary>
        internal uint StreamDirCompression;
        /// <summary>size in bytes of the stream directory when compressed (on disk)</summary>
        internal uint StreamDirSizeCompressed;
        /// <summary>size in bytes of the stream directory when uncompressed (in memory)</summary>
        internal uint StreamDirSizeUncompressed;
        /// <summary>number of compressed chunks</summary>
        internal uint NumChunks;
        /// <summary>size in bytes of the chunk table</summary>
        internal uint ChunkTableSize;

#pragma warning restore 0649

        internal bool IsMagicValid
        {
            get { return Signature.SequenceEqual(ExpectedSignature); }
        }
    }

    internal static class MSFZConstants
    {
        internal const uint COMPRESSION_NONE = 0;
        internal const uint COMPRESSION_ZSTD = 1;
    }

    /// <summary>Allows reading data from an MSFZ stream.</summary>
    internal sealed class MsfzStream : IAddressSpace
    {
        /// <summary>Provides access to the MSFZ file.</summary>
        private readonly MSFZFile _msfzFile;

        /// <summary>The stream index.</summary>
        private readonly uint _stream;

        /// <summary>The size of the stream, in bytes.</summary>
        private readonly uint _size;

        internal MsfzStream(MSFZFile msfzFile, uint stream, uint size)
        {
            _msfzFile = msfzFile;
            _stream = stream;
            _size = size;
        }

        public uint Read(ulong position, byte[] buffer, uint bufferOffset, uint count)
        {
            return _msfzFile.ReadStream(_stream, position, buffer, bufferOffset, count);
        }

        public ulong Length { get { return _size; } }
    }

    internal static class MsfzConstants
    {
        /// <summary>
        /// The special value for the size of a fragment record that indicates the stream is a nil stream,
        /// not an ordinary stream.
        /// </summary>
        public const uint NilFragmentSize = 0xffffffffu;

        /// <summary>
        /// The bitmask that is applied to the FragmentLocation.High value, which specifies that
        /// the fragment is compressed. 1 means compressed, 0 means not compressed.
        /// </summary>
        public const uint FragmentLocationChunkMaskInUInt32 = 1u << 31;

        /// <summary>
        /// The number of uint32 values per fragment record.
        /// </summary>
        public const uint UInt32PerFragmentRecord = 3;
    }

    internal struct MsfzFragmentLocation
    {
        public uint Low;
        public uint High;

        public bool IsCompressedChunk
        {
            get { return (High & MsfzConstants.FragmentLocationChunkMaskInUInt32) != 0u; }
        }

        public ulong UncompressedFileOffset
        {
            get
            {
                Debug.Assert(!IsCompressedChunk);
                return (((ulong)High) << 32) | ((ulong)Low);
            }
        }
    }
}
