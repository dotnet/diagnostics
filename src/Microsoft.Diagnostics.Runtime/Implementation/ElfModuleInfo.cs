// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ElfModuleInfo : ModuleInfo
    {
        private readonly IDataReader _reader;
        private readonly ElfFile? _elf;
        private object? _buildId;
        private Version? _version;

        public override ModuleKind Kind => ModuleKind.Elf;

        public override long ImageSize { get; }

        /// <inheritdoc/>
        public override ImmutableArray<byte> BuildId
        {
            get
            {
                if (_buildId is not null)
                    return (ImmutableArray<byte>)_buildId;

                ImmutableArray<byte> buildId = _elf?.BuildId ?? ImmutableArray<byte>.Empty;
                if (buildId.IsDefault)
                    buildId = ImmutableArray<byte>.Empty;

                _buildId = buildId;
                return buildId;
            }
        }

        public override ulong GetExportSymbolAddress(string symbol)
        {
            if (_elf is null || !_elf.TryGetExportSymbol(symbol, out ulong address))
                return 0;

            return ImageBase + address;
        }

        /// <inheritdoc/>
        public override System.Version Version
        {
            get
            {
                if (_version is not null)
                    return _version;

                if (_elf is null || !_reader.GetVersionInfo(ImageBase, _elf, out System.Version? version))
                    version = new System.Version();

                _version = version;
                return version!;
            }
        }

        public ElfModuleInfo(IDataReader reader, ElfFile? elf, ulong imageBase, long size, string fileName)
            : base(imageBase, fileName)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));

            if (fileName is null)
                throw new ArgumentNullException(nameof(fileName));

            _reader = reader;
            _elf = elf;
            ImageSize = size;
        }
    }
}
