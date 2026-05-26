// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class WasmFileKeyGenerator : KeyGenerator
    {
        /// <summary>
        /// Wasm binary magic number: '\0asm'
        /// </summary>
        private static readonly byte[] s_wasmMagic = new byte[] { 0x00, 0x61, 0x73, 0x6D };

        /// <summary>
        /// Wasm binary format version 1
        /// </summary>
        private static readonly byte[] s_wasmVersion = new byte[] { 0x01, 0x00, 0x00, 0x00 };

        /// <summary>
        /// Custom section ID in Wasm binary format
        /// </summary>
        private const byte CustomSectionId = 0;

        /// <summary>
        /// The name of the custom section containing the build ID
        /// </summary>
        private const string BuildIdSectionName = "build_id";

        /// <summary>
        /// Maximum reasonable build ID length (256 bytes). Protects against
        /// malformed input causing large allocations.
        /// </summary>
        private const int MaxBuildIdLength = 256;

        private readonly SymbolStoreFile _file;
        private byte[] _buildId;
        private bool _parsed;
        private bool _isValid;

        public WasmFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            _file = file ?? throw new ArgumentNullException(nameof(file));
        }

        public override bool IsValid()
        {
            return HasIndexableWasmBuildId();
        }

        public bool HasIndexableWasmBuildId()
        {
            ParseWasmFile();
            return _isValid;
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                if ((flags & KeyTypeFlags.IdentityKey) != 0)
                {
                    yield return GetKey(_file.FileName, _buildId);
                }
            }
        }

        /// <summary>
        /// Create a symbol store key for a Wasm file with a build ID.
        /// </summary>
        /// <param name="path">file name and path</param>
        /// <param name="buildId">build ID bytes from the build_id custom section</param>
        /// <returns>symbol store key</returns>
        public static SymbolStoreKey GetKey(string path, byte[] buildId)
        {
            Debug.Assert(path != null);
            Debug.Assert(buildId != null && buildId.Length > 0);
            string file = GetFileName(path).ToLowerInvariant();
            return BuildKey(path, prefix: null, buildId, file);
        }

        /// <summary>
        /// Parses the Wasm file to validate the header and find the buildId custom section.
        /// </summary>
        private void ParseWasmFile()
        {
            if (_parsed)
            {
                return;
            }
            _parsed = true;
            _isValid = false;

            Stream stream = _file.Stream;
            long prevPosition = stream.Position;
            try
            {
                stream.Position = 0;

                // Validate magic number
                byte[] magic = new byte[4];
                if (stream.Read(magic, 0, 4) != 4)
                {
                    return;
                }
                for (int i = 0; i < 4; i++)
                {
                    if (magic[i] != s_wasmMagic[i])
                    {
                        return;
                    }
                }

                // Validate version
                byte[] version = new byte[4];
                if (stream.Read(version, 0, 4) != 4)
                {
                    return;
                }
                for (int i = 0; i < 4; i++)
                {
                    if (version[i] != s_wasmVersion[i])
                    {
                        return;
                    }
                }

                // Scan sections for the build_id custom section
                while (stream.Position < stream.Length)
                {
                    int sectionId = stream.ReadByte();
                    if (sectionId == -1)
                    {
                        break;
                    }

                    uint sectionSize = ReadLEB128Unsigned(stream);
                    long sectionEnd = stream.Position + sectionSize;

                    // Validate that the section doesn't extend beyond the stream
                    if (sectionEnd > stream.Length)
                    {
                        break;
                    }

                    if (sectionId == CustomSectionId)
                    {
                        string name = ReadWasmString(stream, sectionEnd);
                        if (name == BuildIdSectionName)
                        {
                            // The remainder of the section payload is the build ID
                            int buildIdLength = (int)(sectionEnd - stream.Position);
                            if (buildIdLength > 0 && buildIdLength <= MaxBuildIdLength)
                            {
                                _buildId = new byte[buildIdLength];
                                if (stream.Read(_buildId, 0, buildIdLength) == buildIdLength)
                                {
                                    _isValid = true;
                                    return;
                                }
                            }
                        }
                    }

                    stream.Position = sectionEnd;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is OverflowException || ex is ArgumentOutOfRangeException)
            {
                Tracer.Verbose("Error parsing Wasm file {0}: {1}", _file.FileName, ex.Message);
            }
            finally
            {
                stream.Position = prevPosition;
            }
        }

        /// <summary>
        /// Reads an unsigned LEB128-encoded integer from the stream.
        /// </summary>
        private static uint ReadLEB128Unsigned(Stream stream)
        {
            uint result = 0;
            int shift = 0;

            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1)
                {
                    throw new IOException("Unexpected end of stream reading LEB128 value.");
                }

                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }

                shift += 7;
                if (shift >= 35)
                {
                    throw new OverflowException("LEB128 value too large for uint32.");
                }
            }

            return result;
        }

        /// <summary>
        /// Maximum section name length we'll read. Names longer than this are
        /// skipped since they cannot match the sections we're looking for.
        /// </summary>
        private const int MaxSectionNameLength = 64;

        /// <summary>
        /// Reads a Wasm string (LEB128 length prefix followed by UTF-8 bytes).
        /// Returns null if the string is too long or extends past the section boundary.
        /// </summary>
        private static string ReadWasmString(Stream stream, long sectionEnd)
        {
            uint length = ReadLEB128Unsigned(stream);
            if (length == 0)
            {
                return string.Empty;
            }
            if (length > MaxSectionNameLength || stream.Position + length > sectionEnd)
            {
                return null;
            }

            int stringLength = (int)length;
            byte[] bytes = new byte[stringLength];
            int bytesRead = stream.Read(bytes, 0, stringLength);
            if (bytesRead != stringLength)
            {
                return null;
            }

            return Encoding.UTF8.GetString(bytes);
        }
    }
}
