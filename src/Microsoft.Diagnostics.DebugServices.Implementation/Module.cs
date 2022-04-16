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
            InitializeSymbolFileName = 0x80
        }

        private readonly IDisposable _onChangeEvent;
        private Flags _flags;
        private IEnumerable<PdbFileInfo> _pdbFileInfos;
        protected ImmutableArray<byte> _buildId;
        private PEFile _peFile;
        private string _symbolFileName;

        public readonly ServiceProvider ServiceProvider;

        public Module(ITarget target)
        {
            ServiceProvider = new ServiceProvider();
            ServiceProvider.AddServiceFactoryWithNoCaching<PEFile>(() => GetPEInfo());
            ServiceProvider.AddService<IExportSymbols>(this);

            ServiceProvider.AddServiceFactory<PEReader>(() => {
                if (!IndexTimeStamp.HasValue || !IndexFileSize.HasValue) {
                    return null;
                }
                return Utilities.OpenPEReader(ModuleService.SymbolService.DownloadModuleFile(this));
            });

            if (target.OperatingSystem == OSPlatform.Linux) 
            {
                ServiceProvider.AddServiceFactory<ELFModule>(() => {
                    if (BuildId.IsDefaultOrEmpty) {
                        return null;
                    }
                    return ELFModule.OpenFile(ModuleService.SymbolService.DownloadModuleFile(this));
                });
                ServiceProvider.AddServiceFactory<ELFFile>(() => {
                    Stream stream = ModuleService.MemoryService.CreateMemoryStream();
                    var elfFile = new ELFFile(new StreamAddressSpace(stream), ImageBase, true);
                    return elfFile.IsValid() ? elfFile : null;
                });
            }

            if (target.OperatingSystem == OSPlatform.OSX) 
            {
                ServiceProvider.AddServiceFactory<MachOModule>(() => {
                    if (BuildId.IsDefaultOrEmpty) {
                        return null;
                    }
                    return MachOModule.OpenFile(ModuleService.SymbolService.DownloadModuleFile(this));
                });
                ServiceProvider.AddServiceFactory<MachOFile>(() => {
                    Stream stream = ModuleService.MemoryService.CreateMemoryStream();
                    var machoFile = new MachOFile(new StreamAddressSpace(stream), ImageBase, true);
                    return machoFile.IsValid() ? machoFile : null;
                });
            }

            _onChangeEvent = target.Services.GetService<ISymbolService>()?.OnChangeEvent.Register(() => {
                ServiceProvider.RemoveService(typeof(MachOModule)); 
                ServiceProvider.RemoveService(typeof(ELFModule));
                ServiceProvider.RemoveService(typeof(PEReader));
            });
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

        public IEnumerable<PdbFileInfo> GetPdbFileInfos()
        {
            GetPEInfo();
            Debug.Assert(_pdbFileInfos is not null);
            return _pdbFileInfos;
        }

        public string GetSymbolFileName()
        {
            if (InitializeValue(Flags.InitializeSymbolFileName))
            {
                if (Target.OperatingSystem == OSPlatform.Linux)
                {
                    try
                    {
                        Stream stream = ModuleService.RawMemoryService.CreateMemoryStream();
                        var elfFile = new ELFFile(new StreamAddressSpace(stream), ImageBase, true);
                        if (elfFile.IsValid())
                        {
                            ELFSection section = elfFile.FindSectionByName(".gnu_debuglink");
                            if (section != null)
                            {
                                _symbolFileName = section.Contents.Read<string>(0);
                            }
                        }
                    }
                    catch (Exception ex) when
                       (ex is InvalidVirtualAddressException ||
                        ex is ArgumentOutOfRangeException ||
                        ex is IndexOutOfRangeException ||
                        ex is BadInputFormatException)

                    {
                        Trace.TraceWarning("ELF .gnu_debuglink section in {0}: {1}", this, ex.Message);
                    }
                }
            }
            return _symbolFileName;
        }

        public abstract VersionData GetVersionData();

        public abstract string GetVersionString();

        public abstract string LoadSymbols();

        #endregion

        #region IExportSymbols

        bool IExportSymbols.TryGetSymbolAddress(string name, out ulong address)
        {
            if (Target.OperatingSystem == OSPlatform.Windows)
            {
                PEFile image = Services.GetService<PEFile>();
                if (image is not null)
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
            else if (Target.OperatingSystem == OSPlatform.OSX)
            {
                MachOFile machOFile = Services.GetService<MachOFile>();
                if (machOFile is not null)
                {
                    if (machOFile.Symtab.TryLookupSymbol(name, out ulong offset))
                    {
                        address = machOFile.PreferredVMBaseAddress + offset;
                        return true;
                    }
                    address = 0;
                    return false;
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
                string versionString = GetVersionString();
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
