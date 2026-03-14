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
                MachOFileKeyGenerator[] generators = _machoFatFile.ArchSpecificFiles
                    .Select((MachOFile file) => new MachOFileKeyGenerator(Tracer, file, _path))
                    .ToArray();

                if (generators.Length == 0)
                {
                    Tracer.Verbose("Mach-O fat file `{0}`: missing arch-specific slices. No keys will be generated.", _path);
                    return SymbolStoreKey.EmptyArray;
                }

                return generators.SelectMany((KeyGenerator generator) => generator.GetKeys(flags));
            }
            return SymbolStoreKey.EmptyArray;
        }
    }
}
