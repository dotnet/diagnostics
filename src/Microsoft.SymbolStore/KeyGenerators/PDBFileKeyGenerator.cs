// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.FileFormats;
using Microsoft.FileFormats.PDB;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class PDBFileKeyGenerator : KeyGenerator
    {
        private readonly PDBFile _pdbFile;
        private readonly string _path;

        public PDBFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            StreamAddressSpace dataSource = new(file.Stream);
            _pdbFile = new PDBFile(dataSource);
            _path = file.FileName;
        }

        public override bool IsValid()
        {
            return _pdbFile.IsValid();
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                if ((flags & KeyTypeFlags.IdentityKey) != 0)
                {
                    uint age = _pdbFile.DbiStream.IsValid() ? _pdbFile.DbiAge : _pdbFile.Age;
                    // No format type if legacy Windows PDB (MSF), otherwise, pass container type string (i.e. msfz0)
                    string type = _pdbFile.ContainerKind == PDBContainerKind.MSF ? null : _pdbFile.ContainerKindSpecString;
                    yield return GetKey(_path, _pdbFile.Signature, unchecked((int)age), type, pdbChecksums: null);
                }
            }
        }

        /// <summary>
        /// Create a symbol store key for a Windows PDB.
        /// </summary>
        /// <param name="path">file name and path</param>
        /// <param name="signature">mvid guid</param>
        /// <param name="age">pdb age</param>
        /// <param name="pdbChecksums">Checksums of pdb file. May be null.</param>
        /// <returns>symbol store key</returns>
        public static SymbolStoreKey GetKey(string path, Guid signature, int age, IEnumerable<PdbChecksum> pdbChecksums = null)
        {
            return GetKey(path, signature, age, type: null, pdbChecksums);
        }

        /// <summary>
        /// Create a symbol store key for a Windows PDB or PDZ.
        /// </summary>
        /// <param name="path">file name and path</param>
        /// <param name="signature">mvid guid</param>
        /// <param name="age">pdb age</param>
        /// <param name="type">PDB format type like msfz0 or null</param>
        /// <param name="pdbChecksums">Checksums of pdb file. May be null.</param>
        /// <returns>symbol store key</returns>
        public static SymbolStoreKey GetKey(string path, Guid signature, int age, string type, IEnumerable<PdbChecksum> pdbChecksums = null)
        {
            string file = GetFileName(path).ToLowerInvariant();
            return BuildKey(path, prefix: null, string.Format("{0}{1:x}", signature.ToString("N"), age), type, file, clrSpecialFile: false, pdbChecksums);
        }
    }
}
