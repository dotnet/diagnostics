// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore.KeyGenerators
{
    /// <summary>
    /// Type of keys to generate
    /// </summary>
    [Flags]
    public enum KeyTypeFlags
    {
        /// <summary>
        /// No keys.
        /// </summary>
        None = 0x00,

        /// <summary>
        /// Generate the key of the binary or file itself.
        /// </summary>
        IdentityKey = 0x01,

        /// <summary>
        /// Generate the symbol key of the binary (if one).
        /// </summary>
        SymbolKey = 0x02,

        /// <summary>
        /// Generate the keys for the DAC/SOS modules for a coreclr module.
        /// </summary>
        ClrKeys = 0x04,

        /// <summary>
        /// Forces the key generators to create a Windows Pdb key even when
        /// the DLL debug record entry is marked as portable. Used when both
        /// the Portable and Windows PDBs are available on the symbol server.
        /// </summary>
        ForceWindowsPdbs = 0x08,

        /// <summary>
        /// Generate keys for the host program. This includes the exe or main
        /// module key (usually "dotnet") and an "apphost" symbol index using
        /// the exe or main module's build id for self-contained apps.
        /// </summary>
        HostKeys = 0x10,

        /// <summary>
        /// Return only the DAC (including any cross-OS DACs) and DBI module
        /// keys. Does not including any SOS binaries.
        /// </summary>
        DacDbiKeys = 0x20,

        /// <summary>
        /// Include the runtime modules (coreclr.dll, clrjit.dll, clrgc.dll,
        /// libcoreclr.so, libclrjit.so, libcoreclr.dylib, etc.)
        /// </summary>
        RuntimeKeys = 0x40,

        /// <summary>
        /// Generate the r2r perfmap key of the binary (if one exists).
        /// </summary>
        PerfMapKeys = 0x80
    }

    /// <summary>
    /// The base class for all the key generators. They can be for individual files
    /// or a group of file types.
    /// </summary>
    public abstract class KeyGenerator
    {
        /// <summary>
        /// Trace/logging source
        /// </summary>
        protected readonly ITracer Tracer;

        /// <summary>
        /// Key generator base class.
        /// </summary>
        /// <param name="tracer">logging</param>
        public KeyGenerator(ITracer tracer)
        {
            Tracer = tracer;
        }

        /// <summary>
        /// Returns true if the key generator can get keys for this file or binary.
        /// </summary>
        public abstract bool IsValid();

        /// <summary>
        /// Returns true if file is a mini or core dump.
        /// </summary>
        public virtual bool IsDump()
        {
            return false;
        }

        /// <summary>
        /// Returns the symbol store keys for this file or binary.
        /// </summary>
        /// <param name="flags">what keys to get</param>
        public abstract IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags);

        /// <summary>
        /// Key building helper for "file_name/string_id/file_name" formats.
        /// </summary>
        /// <param name="path">full path of file or binary</param>
        /// <param name="id">id string</param>
        /// <param name="clrSpecialFile">if true, the file is one the clr special files</param>
        /// <param name="pdbChecksums">Checksums of pdb file. May be null.</param>
        /// <returns>key</returns>
        protected static SymbolStoreKey BuildKey(string path, string id, bool clrSpecialFile = false, IEnumerable<PdbChecksum> pdbChecksums = null)
        {
            string file = GetFileName(path).ToLowerInvariant();
            return BuildKey(path, null, id, file, clrSpecialFile, pdbChecksums);
        }

        /// <summary>
        /// Key building helper for "prefix/string_id/file_name" formats.
        /// </summary>
        /// <param name="path">full path of file or binary</param>
        /// <param name="prefix">optional id prefix</param>
        /// <param name="id">build id or uuid</param>
        /// <param name="clrSpecialFile">if true, the file is one the clr special files</param>
        /// <param name="pdbChecksums">Checksums of pdb file. May be null.</param>
        /// <returns>key</returns>
        protected static SymbolStoreKey BuildKey(string path, string prefix, byte[] id, bool clrSpecialFile = false, IEnumerable<PdbChecksum> pdbChecksums = null)
        {
            string file = GetFileName(path).ToLowerInvariant();
            return BuildKey(path, prefix, id, file, clrSpecialFile, pdbChecksums);
        }

        /// <summary>
        /// Key building helper for "prefix/byte_sequence_id/file_name" formats.
        /// </summary>
        /// <param name="path">full path of file or binary</param>
        /// <param name="prefix">optional id prefix</param>
        /// <param name="id">build id or uuid</param>
        /// <param name="file">file name only</param>
        /// <param name="clrSpecialFile">if true, the file is one the clr special files</param>
        /// <param name="pdbChecksums">Checksums of pdb file. May be null.</param>
        /// <returns>key</returns>
        protected static SymbolStoreKey BuildKey(string path, string prefix, byte[] id, string file, bool clrSpecialFile = false, IEnumerable<PdbChecksum> pdbChecksums = null)
        {
            return BuildKey(path, prefix, ToHexString(id), file, clrSpecialFile, pdbChecksums);
        }

        /// <summary>
        /// Key building helper for "prefix/byte_sequence_id/file_name" formats.
        /// </summary>
        /// <param name="path">full path of file or binary</param>
        /// <param name="prefix">optional id prefix</param>
        /// <param name="id">build id or uuid</param>
        /// <param name="file">file name only</param>
        /// <param name="clrSpecialFile">if true, the file is one the clr special files</param>
        /// <param name="pdbChecksums">Checksums of pdb file. May be null.</param>
        /// <returns>key</returns>
        protected static SymbolStoreKey BuildKey(string path, string prefix, string id, string file, bool clrSpecialFile = false, IEnumerable<PdbChecksum> pdbChecksums = null)
        {
            StringBuilder key = new();
            key.Append(file);
            key.Append('/');
            if (prefix != null)
            {
                key.Append(prefix);
                key.Append('-');
            }
            key.Append(id);
            key.Append('/');
            key.Append(file);
            return new SymbolStoreKey(key.ToString(), path, clrSpecialFile, pdbChecksums);
        }

        /// <summary> /// Convert an array of bytes to a lower case hex string.  /// </summary> /// <param name="bytes">array of bytes</param>
        /// <returns>hex string</returns>
        public static string ToHexString(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }

        /// <summary>
        /// The back slashes are changed to forward slashes because Path.GetFileName doesn't work
        /// on Linux /MacOS if there are backslashes. Both back and forward slashes work on Windows.
        /// </summary>
        /// <param name="path">possible windows path</param>
        /// <returns>just the file name</returns>
        internal static string GetFileName(string path)
        {
            return Path.GetFileName(path.Replace('\\', '/'));
        }
    }
}
