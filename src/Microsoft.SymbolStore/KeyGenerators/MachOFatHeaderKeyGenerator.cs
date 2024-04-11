// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.FileFormats;
using Microsoft.FileFormats.MachO;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class MachOFatHeaderKeyGenerator : KeyGenerator
    {
        private readonly MachOFatFile _machoFatFile;
        private readonly string _path;

        public MachOFatHeaderKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            _machoFatFile = new MachOFatFile(new StreamAddressSpace(file.Stream));
            _path = file.FileName;
        }

        public override bool IsValid()
        {
            return _machoFatFile.IsValid();
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                return _machoFatFile.ArchSpecificFiles.Select((file) => new MachOFileKeyGenerator(Tracer, file, _path)).SelectMany((generator) => generator.GetKeys(flags));
            }
            return SymbolStoreKey.EmptyArray;
        }
    }
}
