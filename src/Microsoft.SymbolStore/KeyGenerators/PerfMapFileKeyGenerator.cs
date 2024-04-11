// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.FileFormats.PerfMap;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class PerfMapFileKeyGenerator : KeyGenerator
    {
        private readonly SymbolStoreFile _file;
        private readonly PerfMapFile _perfmapFile;

        public PerfMapFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            _file = file;
            _perfmapFile = new PerfMapFile(_file.Stream);
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (!IsValid() || (flags & KeyTypeFlags.IdentityKey) == 0)
            {
                yield break;
            }
            Debug.Assert(_perfmapFile.Header is not null);

            PerfMapFile.PerfMapHeader header = _perfmapFile.Header;

            if (header.Version > PerfMapFile.MaxKnownPerfMapVersion)
            {
                Tracer.Warning("Trying to get key for PerfMap {0} with version {1}, higher than max known version {2}.",
                    _file.FileName, header.Version, PerfMapFile.MaxKnownPerfMapVersion);
            }
            yield return PerfMapFileKeyGenerator.GetKey(_file.FileName, header.Signature, header.Version);
        }

        public override bool IsValid() => _perfmapFile.IsValid;

        internal static SymbolStoreKey GetKey(string path, byte[] signature, uint version)
        {
            Debug.Assert(path != null);
            Debug.Assert(signature != null);

            string stringSignature = string.Concat(signature.Select(b => b.ToString("x2")));;
            string idComponent = $"r2rmap-v{version}-{stringSignature}";
            return BuildKey(path, idComponent, clrSpecialFile: false, pdbChecksums: null);
        }
    }
}
