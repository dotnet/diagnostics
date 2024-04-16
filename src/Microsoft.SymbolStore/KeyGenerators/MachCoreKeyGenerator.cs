// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.FileFormats;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class MachCoreKeyGenerator : KeyGenerator
    {
        private readonly MachCore _core;

        public MachCoreKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            StreamAddressSpace dataSource = new(file.Stream);
            _core = new MachCore(dataSource);
        }

        public override bool IsValid()
        {
            return _core.IsValid();
        }

        public override bool IsDump()
        {
            return true;
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                return _core.LoadedImages
                    .Select((MachLoadedImage loadedImage) => CreateGenerator(loadedImage))
                    .Where((KeyGenerator generator) => generator != null)
                    .SelectMany((KeyGenerator generator) => generator.GetKeys(flags));
            }
            return SymbolStoreKey.EmptyArray;
        }

        private KeyGenerator CreateGenerator(MachLoadedImage loadedImage)
        {
            try
            {
                if (loadedImage.Image.IsValid())
                {
                    return new MachOFileKeyGenerator(Tracer, loadedImage.Image, loadedImage.Path);
                }
                // TODO - mikem 7/1/17 - need to figure out a better way to determine the file vs loaded layout
                bool layout = loadedImage.Path.StartsWith("/");
                IAddressSpace dataSource = _core.VirtualAddressReader.DataSource;
                PEFile peFile = new(new RelativeAddressSpace(dataSource, loadedImage.LoadAddress, dataSource.Length), layout);
                if (peFile.IsValid())
                {
                    return new PEFileKeyGenerator(Tracer, peFile, loadedImage.Path);
                }
                Tracer.Warning("Unknown Mach core image {0:X16} {1}", loadedImage.LoadAddress, loadedImage.Path);
            }
            catch (InvalidVirtualAddressException ex)
            {
                Tracer.Error("{0}: {1:X16} {2}", ex.Message, loadedImage.LoadAddress, loadedImage.Path);
            }
            return null;
        }
    }
}
