// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class SourceFileKeyGenerator : KeyGenerator
    {
        private readonly SymbolStoreFile _file;

        public SourceFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            _file = file;
        }

        public override bool IsValid()
        {
            return true;
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if ((flags & KeyTypeFlags.IdentityKey) != 0)
            {
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
                byte[] hash = SHA1.Create().ComputeHash(_file.Stream);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
                yield return GetKey(_file.FileName, hash);
            }
        }

        /// <summary>
        /// Create a symbol store key for a source file
        /// </summary>
        /// <param name="path">file name and path</param>
        /// <param name="hash">sha1 hash of the source file</param>
        /// <returns>symbol store key</returns>
        public static SymbolStoreKey GetKey(string path, byte[] hash)
        {
            Debug.Assert(path != null);
            Debug.Assert(hash != null);
            return BuildKey(path, "sha1", hash);
        }
    }
}
