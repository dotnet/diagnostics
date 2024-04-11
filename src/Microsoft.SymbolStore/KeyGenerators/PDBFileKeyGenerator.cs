// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                    if (_pdbFile.DbiStream.IsValid())
                    {
                        yield return GetKey(_path, _pdbFile.Signature, unchecked((int)_pdbFile.DbiAge));
                    }
                    else
                    {
                        yield return GetKey(_path, _pdbFile.Signature, unchecked((int)_pdbFile.Age));
                    }
                }
            }
        }

        /// <summary>
        /// Create a symbol store key for a Windows PDB.
        /// </summary>
        /// <param name="path">file name and path</param>
        /// <param name="signature">mvid guid</param>
        /// <param name="age">pdb age</param>
        /// <returns>symbol store key</returns>
        public static SymbolStoreKey GetKey(string path, Guid signature, int age, IEnumerable<PdbChecksum> pdbChecksums = null)
        {
            Debug.Assert(path != null);
            Debug.Assert(signature != null);
            return BuildKey(path, string.Format("{0}{1:x}", signature.ToString("N"), age));
        }
    }
}
