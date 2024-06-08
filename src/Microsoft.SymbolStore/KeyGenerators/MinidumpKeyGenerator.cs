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

        public MinidumpKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            _dataSource = new StreamAddressSpace(file.Stream);
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
                    return dump.LoadedImages
                        .Select((MinidumpLoadedImage loadedImage) => new PEFileKeyGenerator(Tracer, loadedImage.Image, loadedImage.ModuleName))
                        .SelectMany((KeyGenerator generator) => generator.GetKeys(flags));
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
