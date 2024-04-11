// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class ELFCoreKeyGenerator : KeyGenerator
    {
        private readonly ELFCoreFile _core;

        public ELFCoreKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : base(tracer)
        {
            StreamAddressSpace dataSource = new(file.Stream);
            _core = new ELFCoreFile(dataSource);
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
                    .Select((ELFLoadedImage loadedImage) => CreateGenerator(loadedImage))
                    .Where((KeyGenerator generator) => generator != null)
                    .SelectMany((KeyGenerator generator) => generator.GetKeys(flags));
            }
            return SymbolStoreKey.EmptyArray;
        }

        private KeyGenerator CreateGenerator(ELFLoadedImage loadedImage)
        {
            try
            {
                if (loadedImage.Image.IsValid())
                {
                    return new ELFFileKeyGenerator(Tracer, loadedImage.Image, loadedImage.Path);
                }
                // TODO - mikem 7/1/17 - need to figure out a better way to determine the file vs loaded layout
                bool layout = loadedImage.Path.StartsWith("/");
                RelativeAddressSpace reader = new(_core.DataSource, loadedImage.LoadAddress, _core.DataSource.Length);
                PEFile peFile = new(reader, layout);
                if (peFile.IsValid())
                {
                    return new PEFileKeyGenerator(Tracer, peFile, loadedImage.Path);
                }
                // Check if this is a macho module in a ELF 5.0.x MacOS dump
                MachOFile machOFile = new(reader, 0, true);
                if (machOFile.IsValid())
                {
                    return new MachOFileKeyGenerator(Tracer, machOFile, loadedImage.Path);
                }
                Tracer.Warning("Unknown ELF core image {0:X16} {1}", loadedImage.LoadAddress, loadedImage.Path);
            }
            catch (InvalidVirtualAddressException ex)
            {
                Tracer.Error("{0}: {1:X16} {2}", ex.Message, loadedImage.LoadAddress, loadedImage.Path);
            }
            return null;
        }
    }
}
