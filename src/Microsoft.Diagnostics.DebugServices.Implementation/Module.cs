// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.PE;

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
            InitializeVersion = 0x10,
            InitializeProductVersion = 0x20,
            InitializeSymbolFileName = 0x40
        }

        private Flags _flags;
        private IEnumerable<PdbFileInfo> _pdbFileInfos;
        private string _symbolFileName;

        protected ImmutableArray<byte> _buildId;
        protected readonly ServiceContainer _serviceContainer;

        public Module(IServiceProvider services)
        {
            ServiceContainerFactory containerFactory = services.GetService<IServiceManager>().CreateServiceContainerFactory(ServiceScope.Module, services);
            containerFactory.AddServiceFactory<PEFile>((services) => ModuleService.GetPEInfo(ImageBase, ImageSize, out _pdbFileInfos, ref _flags));
            _serviceContainer = containerFactory.Build();
            _serviceContainer.AddService<IModule>(this);
            _serviceContainer.AddService<IExportSymbols>(this);
        }

        public virtual void Dispose()
        {
            _serviceContainer.RemoveService(typeof(IModule));
            _serviceContainer.RemoveService(typeof(IExportSymbols));
            _serviceContainer.DisposeServices();
        }

        #region IModule

        public ITarget Target => ModuleService.Target;

        public IServiceProvider Services => _serviceContainer;

        public virtual int ModuleIndex { get; protected set; }

        public virtual string FileName { get; protected set; }

        public virtual ulong ImageBase { get; protected set; }

        public virtual ulong ImageSize { get; protected set; }

        public virtual uint? IndexFileSize { get; protected set; }

        public virtual uint? IndexTimeStamp { get; protected set; }

        public bool IsPEImage
        {
            get
            {
                // For Windows targets, we can always assume that all the modules are PEs.
                if (Target.OperatingSystem == OSPlatform.Windows)
                {
                    return true;
                }
                Services.GetService<PEFile>();
                return (_flags & Flags.IsPEImage) != 0;
            }
        }

        public bool IsManaged
        {
            get
            {
                Services.GetService<PEFile>();
                return (_flags & Flags.IsManaged) != 0;
            }
        }

        public bool? IsFileLayout
        {
            get
            {
                Services.GetService<PEFile>();
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
            Services.GetService<PEFile>();
            Debug.Assert(_pdbFileInfos is not null);
            return _pdbFileInfos;
        }

        public string GetSymbolFileName()
        {
            if (InitializeValue(Flags.InitializeSymbolFileName))
            {
                if (ImageSize > 0 && Target.OperatingSystem == OSPlatform.Linux)
                {
                    try
                    {
                        Stream stream = ModuleService.MemoryService.CreateMemoryStream();
                        ELFFile elfFile = new(new StreamAddressSpace(stream), ImageBase, true);
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
                       (ex is InvalidVirtualAddressException or
                        ArgumentOutOfRangeException or
                        IndexOutOfRangeException or
                        OverflowException or
                        BadInputFormatException)
                    {
                        Trace.TraceWarning("ELF .gnu_debuglink section in {0}: {1}", this, ex.Message);
                    }
                }
            }
            return _symbolFileName;
        }

        public abstract Version GetVersionData();

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
                if (ImageSize > 0)
                {
                    ModuleInfo module = ModuleInfo.TryCreate(Services.GetService<IDataReader>(), ImageBase, FileName);
                    if (module is not null)
                    {
                        address = module.GetExportSymbolAddress(name);
                        return address != 0;
                    }
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

        protected Version GetVersionInner()
        {
            Version version = null;

            PEFile peFile = Services.GetService<PEFile>();
            if (peFile is not null)
            {
                try
                {
                    VsFixedFileInfo fileInfo = peFile.VersionInfo;
                    if (fileInfo != null)
                    {
                        version = fileInfo.ToVersion();
                    }
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException or BadInputFormatException)
                {
                    Trace.TraceError($"GetVersion: exception {ex.Message}");
                }
            }
            else
            {
                // If we can't get the version from the PE, search for version string embedded in the module data
                version = Utilities.ParseVersionString(GetVersionString());
            }

            return version;
        }

        protected string GetVersionStringInner()
        {
            if (ModuleService.Target.OperatingSystem != OSPlatform.Windows && !IsPEImage)
            {
                return ModuleService.GetVersionString(this);
            }
            return null;
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
