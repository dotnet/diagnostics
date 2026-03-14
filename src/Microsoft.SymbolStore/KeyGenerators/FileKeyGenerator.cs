// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.SymbolStore.KeyGenerators
{
    /// <summary>
    /// Generates a key for any kind of file (ELF core/MachO core/Minidump,
    /// ELF/MachO/PE binary, PDB, etc).
    /// </summary>
    public class FileKeyGenerator : KeyGenerator
    {
        private readonly SymbolStoreFile _file;

        public FileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            _file = file;
        }

        public override bool IsValid()
        {
            return GetGenerators().Any((generator) => generator.IsValid());
        }

        public override bool IsDump()
        {
            return GetGenerators().Any((generator) => generator.IsValid() && generator.IsDump());
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            foreach (KeyGenerator generator in GetGenerators())
            {
                _file.Stream.Position = 0;
                if (generator.IsValid())
                {
                    return generator.GetKeys(flags);
                }
            }
            Tracer.Verbose("Unknown file type: {0}", _file.FileName);
            return SymbolStoreKey.EmptyArray;
        }

        private IEnumerable<KeyGenerator> GetGenerators()
        {
            if (_file.Stream.Length > 0)
            {
                yield return new ELFCoreKeyGenerator(Tracer, _file);
                yield return new MachCoreKeyGenerator(Tracer, _file);
                yield return new MinidumpKeyGenerator(Tracer, _file);
                yield return new ELFFileKeyGenerator(Tracer, _file);
                yield return new PEFileKeyGenerator(Tracer, _file);
                yield return new MachOFatHeaderKeyGenerator(Tracer, _file);
                yield return new MachOFileKeyGenerator(Tracer, _file);
                yield return new PDBFileKeyGenerator(Tracer, _file);
                yield return new PortablePDBFileKeyGenerator(Tracer, _file);
                yield return new PerfMapFileKeyGenerator(Tracer, _file);
            }
        }
    }
}
