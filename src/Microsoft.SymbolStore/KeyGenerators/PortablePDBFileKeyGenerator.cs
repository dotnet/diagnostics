// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class PortablePDBFileKeyGenerator : KeyGenerator
    {
        private readonly SymbolStoreFile _file;

        public PortablePDBFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            _file = file;
        }

        public override bool IsValid()
        {
            try
            {
                _file.Stream.Position = 0;
                using (MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(_file.Stream, MetadataStreamOptions.LeaveOpen))
                {
                    MetadataReader reader = provider.GetMetadataReader();
                    return true;
                }
            }
            catch (BadImageFormatException)
            {
            }
            return false;
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if ((flags & KeyTypeFlags.IdentityKey) != 0)
            {
                SymbolStoreKey key = null;

                try
                {
                    _file.Stream.Position = 0;
                    using (MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(_file.Stream, MetadataStreamOptions.LeaveOpen))
                    {
                        MetadataReader reader = provider.GetMetadataReader();
                        BlobContentId blob = new(reader.DebugMetadataHeader.Id);
                        if ((flags & KeyTypeFlags.ForceWindowsPdbs) == 0)
                        {
                            key = GetKey(_file.FileName, blob.Guid);
                        }
                        else
                        {
                            // Force the Windows PDB index
                            key = PDBFileKeyGenerator.GetKey(_file.FileName, blob.Guid, 1);
                        }
                    }
                }
                catch (BadImageFormatException ex)
                {
                    Tracer.Warning("PortablePDBFileKeyGenerator {0}", ex.Message);
                }

                if (key != null)
                {
                    yield return key;
                }
            }
        }

        /// <summary>
        /// Create a symbol store key for a Portable PDB.
        /// </summary>
        /// <param name="path">file name and path</param>
        /// <param name="pdbId">pdb guid</param>
        /// <returns>symbol store key</returns>
        public static SymbolStoreKey GetKey(string path, Guid pdbId, IEnumerable<PdbChecksum> pdbChecksums = null)
        {
            Debug.Assert(path != null);
            Debug.Assert(pdbId != null);
            return BuildKey(path, pdbId.ToString("N") + "FFFFFFFF", clrSpecialFile: false, pdbChecksums);
        }
    }
}
