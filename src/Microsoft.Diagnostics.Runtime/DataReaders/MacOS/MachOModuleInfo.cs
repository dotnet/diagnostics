// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using Microsoft.Diagnostics.Runtime.MacOS.Structs;

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal sealed class MachOModuleInfo : ModuleInfo
    {
        private readonly MachOModule? _module;
        private Version? _version;
        private readonly ulong _imageSize;

        public override ModuleKind Kind => ModuleKind.MachO;

        public override long ImageSize => _imageSize > long.MaxValue ? long.MaxValue : unchecked((long)_imageSize);

        public override Version Version
        {
            get
            {
                if (_version is not null)
                    return _version;

                if (_module is not null)
                    foreach (Segment64LoadCommand segment in _module.EnumerateSegments())
                        if (((segment.InitProt & Segment64LoadCommand.VmProtWrite) != 0) && !segment.Name.Equals("__LINKEDIT"))
                            if (_module.DataReader.GetVersionInfo(_module.LoadBias + segment.VMAddr, segment.VMSize, out Version? version))
                                _version = version;

                return _version ?? new Version();
            }
        }

        public override ImmutableArray<byte> BuildId
        {
            get
            {
                if (_module is not null)
                    return _module.BuildId;

                return ImmutableArray<byte>.Empty;
            }
        }


        public override ulong GetExportSymbolAddress(string symbol)
        {
            if (_module is null)
                return 0;

            if (_module.TryLookupSymbol(symbol, out ulong symbolAddress))
                return symbolAddress;

            return 0;
        }

        public MachOModuleInfo(MachOModule? module, ulong imageBase, string fileName, Version? version, ulong imageSize)
            : base(imageBase, fileName)
        {
            _module = module;
            _version = version;
            _imageSize = imageSize;
        }
    }
}
