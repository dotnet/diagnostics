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

        private const string IdentityPrefix = "wasm-buildid";
        private const string SymbolPrefix = "wasm-buildid-sym";

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
            ParseWasmFile();
            return _isValid;
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                if ((flags & KeyTypeFlags.IdentityKey) != 0)
                {
                    bool isSymbolFile = IsSymbolFile();
                    yield return GetKey(_file.FileName, _buildId, isSymbolFile);
                }
            }
        }

        /// <summary>
        /// Create a symbol store key for a Wasm file with a build ID.
        /// </summary>
        /// <param name="path">file name and path</param>
        /// <param name="buildId">build ID bytes from the buildId custom section</param>
        /// <param name="symbolFile">if true, this is a symbol file (contains DWARF sections)</param>
        /// <returns>symbol store key</returns>
        public static SymbolStoreKey GetKey(string path, byte[] buildId, bool symbolFile)
        {
            Debug.Assert(path != null);
            Debug.Assert(buildId != null && buildId.Length > 0);
            string prefix = symbolFile ? SymbolPrefix : IdentityPrefix;
            return BuildKey(path, prefix, buildId);
        }

        /// <summary>
        /// Determines whether this Wasm module is a symbol file by checking
        /// for the presence of DWARF debug custom sections.
        /// </summary>
        private bool IsSymbolFile()
        {
            try
            {
                Stream stream = _file.Stream;
                stream.Position = 8; // Skip magic and version

                while (stream.Position < stream.Length)
                {
                    int sectionId = stream.ReadByte();
                    if (sectionId == -1)
                    {
                        break;
                    }

                    uint sectionSize = ReadLEB128Unsigned(stream);
                    long sectionEnd = stream.Position + sectionSize;

                    if (sectionId == CustomSectionId)
                    {
                        long nameStart = stream.Position;
                        string name = ReadWasmString(stream);
                        if (name != null && name.StartsWith(".debug_"))
                        {
                            return true;
                        }
                    }

                    stream.Position = sectionEnd;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is OverflowException || ex is ArgumentOutOfRangeException)
            {
                Tracer.Verbose("Error checking Wasm symbol sections in {0}: {1}", _file.FileName, ex.Message);
            }

            return false;
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

            try
            {
                Stream stream = _file.Stream;
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

                // Scan sections for the buildId custom section
                while (stream.Position < stream.Length)
                {
                    int sectionId = stream.ReadByte();
                    if (sectionId == -1)
                    {
                        break;
                    }

                    uint sectionSize = ReadLEB128Unsigned(stream);
                    long sectionEnd = stream.Position + sectionSize;

                    if (sectionId == CustomSectionId)
                    {
                        long nameStart = stream.Position;
                        string name = ReadWasmString(stream);
                        if (name == BuildIdSectionName)
                        {
                            // The remainder of the section payload is the build ID
                            int buildIdLength = (int)(sectionEnd - stream.Position);
                            if (buildIdLength > 0)
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
        /// Reads a Wasm string (LEB128 length prefix followed by UTF-8 bytes).
        /// </summary>
        private static string ReadWasmString(Stream stream)
        {
            uint length = ReadLEB128Unsigned(stream);
            if (length == 0)
            {
                return string.Empty;
            }
            if (length > int.MaxValue)
            {
                return null;
            }

            byte[] bytes = new byte[length];
            int bytesRead = stream.Read(bytes, 0, (int)length);
            if (bytesRead != (int)length)
            {
                return null;
            }

            return Encoding.UTF8.GetString(bytes);
        }
    }
}
