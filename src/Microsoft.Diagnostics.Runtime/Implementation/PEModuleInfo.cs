// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    /// <summary>
    /// A helper to implement <see cref="ModuleInfo"/> for PEImages.
    /// </summary>
    internal sealed class PEModuleInfo : ModuleInfo
    {
        private readonly IDataReader _dataReader;
        private readonly bool _isVirtual;

        private int _timestamp;
        private int _filesize;

        private bool _loaded;
        private PdbInfo? _pdb;
        private bool? _isManaged;
        private PEImage? _peImage;
        private Version? _version;

        public override ModuleKind Kind => ModuleKind.PortableExecutable;

        internal PEImage? GetPEImage()
        {
            if (_peImage is not null || _loaded)
                return _peImage;

            try
            {
                PEImage image = new(new ReadVirtualStream(_dataReader, (long)ImageBase, int.MaxValue), leaveOpen: false, isVirtual: _isVirtual);
                if (!image.IsValid)
                {
                    image.Dispose();
                    image = new PEImage(new ReadVirtualStream(_dataReader, (long)ImageBase, int.MaxValue), leaveOpen: false, isVirtual: !_isVirtual);
                }

                if (image.IsValid)
                {
                    Interlocked.CompareExchange(ref _peImage, image, null);
                    _loaded = true;
                    return _peImage;
                }

                image.Dispose();
            }
            catch
            {
            }

            _loaded = true;
            return null;
        }

        /// <inheritdoc/>
        public override System.Version Version
        {
            get
            {
                if (_version is not null)
                    return _version;

                System.Version version = GetPEImage()?.GetFileVersionInfo()?.Version ?? new System.Version();
                _version = version;
                return version;
            }
        }

        /// <inheritdoc/>
        public override PdbInfo? Pdb
        {
            get
            {
                if (_pdb is not null)
                    return _pdb;

                PdbInfo? pdb = GetPEImage()?.DefaultPdb;
                _pdb = pdb;
                return pdb;
            }
        }

        /// <inheritdoc/>
        public override bool IsManaged
        {
            get
            {
                if (_isManaged is bool result)
                    return result;

                result = GetPEImage()?.IsManaged ?? false;
                _isManaged = result;
                return result;
            }
        }

        /// <inheritdoc/>
        public override int IndexFileSize
        {
            get
            {
                if (_timestamp == 0 && _filesize == 0)
                {
                    PEImage? img = GetPEImage();
                    if (img is not null)
                    {
                        _timestamp = img.IndexTimeStamp;
                        _filesize = img.IndexFileSize;
                    }
                }

                return _filesize;
            }
        }

        /// <inheritdoc/>
        public override int IndexTimeStamp
        {
            get
            {
                if (_timestamp == 0 && _filesize == 0)
                {
                    PEImage? img = GetPEImage();
                    if (img is not null)
                    {
                        _timestamp = img.IndexTimeStamp;
                        _filesize = img.IndexFileSize;
                    }
                }

                return _timestamp;
            }
        }

        public override ulong GetExportSymbolAddress(string symbol)
        {
            PEImage? img = GetPEImage();
            if (img is not null && img.TryGetExportSymbol(symbol, out ulong offset) && offset != 0)
                return ImageBase + offset;

            return 0;
        }

        protected override void TrySetProperties(int indexFileSize, int indexTimeStamp, Version? version)
        {
            _filesize = indexFileSize;
            _timestamp = indexTimeStamp;
            if (_version is null && version is not null)
                _version = version;
        }

        public override IResourceNode? ResourceRoot => GetPEImage()?.Resources;

        public PEModuleInfo(IDataReader dataReader, ulong imageBase, string fileName, bool isVirtualHint)
            : base(imageBase, fileName)
        {
            if (dataReader is null)
                throw new ArgumentNullException(nameof(dataReader));

            if (fileName is null)
                throw new ArgumentNullException(nameof(fileName));

            _dataReader = dataReader;
            _isVirtual = isVirtualHint;
        }

        public PEModuleInfo(IDataReader dataReader, ulong imageBase, string fileName, bool isVirtual, int timestamp, int filesize, Version? version = null)
            : this(dataReader, imageBase, fileName, isVirtual)
        {
            _timestamp = timestamp;
            _filesize = filesize;
            _version = version;
        }
    }
}