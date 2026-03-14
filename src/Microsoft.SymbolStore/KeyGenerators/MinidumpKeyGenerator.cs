// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.FileFormats;
using Microsoft.FileFormats.Minidump;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class MinidumpKeyGenerator : KeyGenerator
    {
        private readonly IAddressSpace _dataSource;
        private readonly string _path;

        public MinidumpKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            _dataSource = new StreamAddressSpace(file.Stream);
            _path = file.FileName;
        }

        public override bool IsValid()
        {
            return Minidump.IsValid(_dataSource);
        }

        public override bool IsDump()
        {
            return true;
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                try
                {
                    Minidump dump = new(_dataSource);
                    KeyGenerator[] generators = dump.LoadedImages
                        .Select((MinidumpLoadedImage loadedImage) => new PEFileKeyGenerator(Tracer, loadedImage.Image, loadedImage.ModuleName))
                        .Where((KeyGenerator g) => g != null && g.IsValid())
                        .ToArray();

                    if (generators.Length == 0)
                    {
                        Tracer.Verbose("Minidump file `{0}`: missing valid module images. No keys will be generated.", _path);
                        return SymbolStoreKey.EmptyArray;
                    }

                    return generators.SelectMany((KeyGenerator generator) => generator.GetKeys(flags));
                }
                catch (InvalidVirtualAddressException ex)
                {
                    Tracer.Error("Minidump {0}", ex.Message);
                }
            }
            return SymbolStoreKey.EmptyArray;
        }
    }
}
