// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Module base implementation
    /// </summary>
    public abstract class Module : IModule, IExportSymbols, IDisposable
    {
        [Flags]
        public enum Flags : byte
        {
            None = 0x00,
            IsPEImage = 0x01,
            IsManaged = 0x02,
            IsFileLayout = 0x04,
            IsLoadedLayout = 0x08,
            InitializePEInfo = 0x10,
            InitializeVersion = 0x20,
            InitializeProductVersion = 0x40,
        }

        private readonly IDisposable _onChangeEvent;
        private Flags _flags;
        private IEnumerable<PdbFileInfo> _pdbFileInfos;
        protected ImmutableArray<byte> _buildId;
        private PEFile _peFile;

        public readonly ServiceProvider ServiceProvider;

        public Module(ITarget target)
        {
            ServiceProvider = new ServiceProvider();
            ServiceProvider.AddServiceFactoryWithNoCaching<PEFile>(() => GetPEInfo());

            ServiceProvider.AddServiceFactory<PEReader>(() => Utilities.OpenPEReader(ModuleService.SymbolService.DownloadModule(this)));
            if (target.OperatingSystem == OSPlatform.Linux) {
                ServiceProvider.AddServiceFactory<ELFFile>(() => Utilities.OpenELFFile(ModuleService.SymbolService.DownloadModule(this)));
            }
            if (target.OperatingSystem == OSPlatform.OSX) {
                ServiceProvider.AddServiceFactory<MachOFile>(() => Utilities.OpenMachOFile(ModuleService.SymbolService.DownloadModule(this)));
            }
            _onChangeEvent = target.Services.GetService<ISymbolService>()?.OnChangeEvent.Register(() => {
                ServiceProvider.RemoveService(typeof(MachOFile)); 
                ServiceProvider.RemoveService(typeof(ELFFile));
                ServiceProvider.RemoveService(typeof(PEReader));
            });
            ServiceProvider.AddService<IExportSymbols>(this);
         }

        public void Dispose()
        {
            _onChangeEvent?.Dispose();
        }

        #region IModule

        public ITarget Target => ModuleService.Target;

        public IServiceProvider Services => ServiceProvider;

        public abstract int ModuleIndex { get; }

        public abstract string FileName { get; }

        public abstract ulong ImageBase { get; }

        public abstract ulong ImageSize { get; }

        public abstract uint? IndexFileSize { get; }

        public abstract uint? IndexTimeStamp { get; }

        public bool IsPEImage
        {
            get
            {
                // For Windows targets, we can always assume that all the modules are PEs.
                if (Target.OperatingSystem == OSPlatform.Windows)
                {
                    return true;
                }
                else
                {
                    GetPEInfo();
                    return (_flags & Flags.IsPEImage) != 0;
                }
            }
        }

        public bool IsManaged
        {
            get
            {
                GetPEInfo();
                return (_flags & Flags.IsManaged) != 0;
            }
        }

        public bool? IsFileLayout
        {
            get
            {
                GetPEInfo();
                if ((_flags & Flags.IsFileLayout) != 0)
                {
                    return true;
                }
                if ((_flags & Flags.IsLoadedLayout) != 0)
                {
                    return false;
                }
                // Native Windows dlls default to file layout
                if ((_flags & Flags.IsManaged) == 0 && Target.OperatingSystem == OSPlatform.Windows)
                {
                    return false;
                }
                return null;
            }
        }

        public IEnumerable<PdbFileInfo> PdbFileInfos
        {
            get
            {
                GetPEInfo();
                Debug.Assert(_pdbFileInfos is not null);
                return _pdbFileInfos;
            }
        }

        public virtual ImmutableArray<byte> BuildId
        {
            get
            {
                if (_buildId.IsDefault)
                {
                    byte[] id = ModuleService.GetBuildId(ImageBase);
                    if (id != null)
                    {
                        _buildId = id.ToImmutableArray();
                    }
                    else
                    {
                        _buildId = ImmutableArray<byte>.Empty;
                    }
                }
                return _buildId;
            }
        }

        public abstract VersionData VersionData { get; }

        public abstract string VersionString { get; }

        #endregion

        #region IExportSymbols

        bool IExportSymbols.TryGetSymbolAddress(string name, out ulong address)
        {
            if (Target.OperatingSystem == OSPlatform.Windows)
            {
                Stream stream = ModuleService.MemoryService.CreateMemoryStream(ImageBase, ImageSize);
                PEFile image = new(new StreamAddressSpace(stream), isDataSourceVirtualAddressSpace: true);
                if (image.IsValid())
                { 
                    if (image.TryGetExportSymbol(name, out ulong offset))
                    {
                        address = ImageBase + offset;
                        return true;
                    }
                    address = 0;
                    return false;
                }
            }
            else if (Target.OperatingSystem == OSPlatform.Linux)
            {
                try
                {
                    Stream stream = ModuleService.MemoryService.CreateMemoryStream(ImageBase, ImageSize);
                    ElfFile elfFile = new(stream, position: ImageBase, leaveOpen: false, isVirtual: true);
                    if (elfFile.Header.IsValid)
                    {
                        if (elfFile.TryGetExportSymbol(name, out ulong offset))
                        {
                            address = ImageBase + offset;
                            return true;
                        }
                        address = 0;
                        return false;
                    }
                }
                catch (InvalidDataException)
                {
                }
            }
            return TryGetSymbolAddressInner(name, out address);
        }

        protected virtual bool TryGetSymbolAddressInner(string name, out ulong address)
        {
            address = 0;
            return false;
        }

        #endregion

        protected VersionData GetVersion()
        {
            VersionData versionData = null;

            PEFile peFile = GetPEInfo();
            if (peFile != null)
            {
                try
                {
                    VsFixedFileInfo fileInfo = peFile.VersionInfo;
                    if (fileInfo != null)
                    {
                        versionData = fileInfo.ToVersionData();
                    }
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException)
                {
                    Trace.TraceError($"GetVersion: exception {ex.Message}");
                }
            }
            else 
            {
                // If we can't get the version from the PE, search for version string embedded in the module data
                string versionString = VersionString;
                if (versionString != null)
                {
                    int spaceIndex = versionString.IndexOf(' ');
                    if (spaceIndex < 0)
                    {
                        // It is probably a private build version that doesn't end with a space (no commit id after)
                        spaceIndex = versionString.Length;
                    }
                    if (spaceIndex > 0)
                    {
                        if (versionString[spaceIndex - 1] == '.')
                        {
                            spaceIndex--;
                        }
                        string versionToParse = versionString.Substring(0, spaceIndex);
                        try
                        {
                            Version version = System.Version.Parse(versionToParse);
                            versionData = new VersionData(version.Major, version.Minor, version.Build, version.Revision);
                        }
                        catch (ArgumentException ex)
                        {
                            Trace.TraceError($"Module.GetVersion FAILURE: '{versionToParse}' '{versionString}' {ex}");
                        }
                    }
                }
                else
                {
                    Trace.TraceInformation($"Module.GetVersion no version string");
                }
            }

            return versionData;
        }

        protected PEFile GetPEInfo()
        {
            if (InitializeValue(Flags.InitializePEInfo))
            {
                _peFile = ModuleService.GetPEInfo(ImageBase, ImageSize, out _pdbFileInfos, ref _flags);
            }
            return _peFile;
        }

        protected bool InitializeValue(Flags flag)
        {
            if ((_flags & flag) == 0)
            {
                _flags |= flag;
                return true;
            }
            return false;
        }

        protected abstract ModuleService ModuleService { get; }

        public override bool Equals(object obj)
        {
            IModule module = (IModule)obj;
            return Target == module.Target && ImageBase == module.ImageBase;
        }

        public override int GetHashCode()
        {
            return Utilities.CombineHashCodes(Target.GetHashCode(), ImageBase.GetHashCode());
        }

        public override string ToString()
        {
            return $"#{ModuleIndex} {ImageBase:X16} {_flags} {FileName ?? ""}";
        }
    }
}
