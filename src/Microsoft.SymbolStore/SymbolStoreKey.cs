// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore
{
    /// <summary>
    /// Symbol store key information
    /// </summary>
    public sealed class SymbolStoreKey
    {
        /// <summary>
        /// Symbol server index
        /// </summary>
        public readonly string Index;

        /// <summary>
        /// Full path name
        /// </summary>
        public readonly string FullPathName;

        /// <summary>
        /// If true, this file is one of the clr special files like the DAC or SOS, but
        /// the key is the normal identity key for this file.
        /// </summary>
        public readonly bool IsClrSpecialFile;

        /// <summary>
        /// Empty array of keys
        /// </summary>
        public static SymbolStoreKey[] EmptyArray = Array.Empty<SymbolStoreKey>();

        /// <summary>
        /// The checksums of the pdb file (if any)
        /// </summary>
        public readonly IEnumerable<PdbChecksum> PdbChecksums;

        /// <summary>
        /// Create key instance.
        /// </summary>
        /// <param name="index">index to lookup on symbol server</param>
        /// <param name="fullPathName">the full path name of the file</param>
        /// <param name="clrSpecialFile">if true, the file is one the clr special files</param>
        /// <param name="pdbChecksums">if true, the file is one the clr special files</param>
        public SymbolStoreKey(string index, string fullPathName, bool clrSpecialFile = false, IEnumerable<PdbChecksum> pdbChecksums = null)
        {
            Debug.Assert(index != null && fullPathName != null);
            Index = index;
            FullPathName = fullPathName;
            IsClrSpecialFile = clrSpecialFile;
            PdbChecksums = pdbChecksums ?? Enumerable.Empty<PdbChecksum>();
        }

        /// <summary>
        /// Returns the first two parts of the index tuple. Allows a different file name
        /// to be appended to this symbol key. Includes the trailing "/".
        /// </summary>
        public string IndexPrefix
        {
            get { return Index.Substring(0, Index.LastIndexOf("/") + 1); }
        }

        /// <summary>
        /// Returns the hash of the index.
        /// </summary>
        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }

        /// <summary>
        /// Only the index is compared or hashed. The FileName is already
        /// part of the index.
        /// </summary>
        public override bool Equals(object obj)
        {
            SymbolStoreKey right = (SymbolStoreKey)obj;
            return string.Equals(Index, right.Index);
        }

        private static HashSet<char> s_invalidChars = new(Path.GetInvalidFileNameChars());

        /// <summary>
        /// Validates a symbol index.
        ///
        /// SSQP theoretically supports a broader set of keys, but in order to ensure that all the keys
        /// play well with the caching scheme we enforce additional requirements (that all current key
        /// conventions also meet).
        /// </summary>
        /// <param name="index">symbol key index</param>
        /// <returns>true if valid</returns>
        public static bool IsKeyValid(string index)
        {
            string[] parts = index.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) {
                return false;
            }
            for (int i = 0; i < 3; i++)
            {
                foreach (char c in parts[i])
                {
                    if (char.IsLetterOrDigit(c)) {
                        continue;
                    }
                    if (!s_invalidChars.Contains(c)) {
                        continue;
                    }
                    return false;
                }
                // We need to support files with . in the name, but we don't want identifiers that
                // are meaningful to the filesystem
                if (parts[i] == "." || parts[i] == "..") {
                    return false;
                }
            }
            return true;
        }
    }
}
