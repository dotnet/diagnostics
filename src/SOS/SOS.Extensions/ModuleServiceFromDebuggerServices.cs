// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions
{
    /// <summary>
    /// Module service implementation for the native debugger services
    /// </summary>
    internal sealed class ModuleServiceFromDebuggerServices : ModuleService
    {
        private sealed class FieldFromDebuggerServices : IField
        {
            public FieldFromDebuggerServices(IType type, string fieldName, uint offset)
            {
                Type = type;
                Name = fieldName;
                Offset = offset;
            }
            public IType Type { get; }

            public string Name { get; }

            public uint Offset { get; }
        }

        private sealed class TypeFromDebuggerServices : IType
        {
            private ModuleServiceFromDebuggerServices _moduleService;
            private ulong _typeId;

            public TypeFromDebuggerServices(ModuleServiceFromDebuggerServices moduleService, IModule module, ulong typeId, string typeName)
            {
                _moduleService = moduleService;
                _typeId = typeId;
                Module = module;
                Name = typeName;
            }

            public IModule Module { get; }

            public string Name { get; }

            public List<IField> Fields => throw new NotImplementedException();

            public bool TryGetField(string fieldName, out IField field)
            {
                HResult hr = _moduleService._debuggerServices.GetFieldOffset(Module.ModuleIndex, _typeId, Name, fieldName, out uint offset);
                if (hr != HResult.S_OK)
                {
                    field = null;
                    return false;
                }
                field = new FieldFromDebuggerServices(this, fieldName, offset);
                return true;
            }
        }

        private sealed class ModuleFromDebuggerServices : Module, IModuleSymbols
        {
            // This is what dbgeng/IDebuggerServices returns for non-PE modules that don't have a timestamp
            private const uint InvalidTimeStamp = 0xFFFFFFFE;

            private readonly ModuleServiceFromDebuggerServices _moduleService;
            private Version _version;
            private string _versionString;
            private SymbolStatus _symbolStatus = SymbolStatus.Unknown;

            public ModuleFromDebuggerServices(
                ModuleServiceFromDebuggerServices moduleService,
                int moduleIndex,
                string imageName,
                ulong imageBase,
                ulong imageSize,
                uint indexFileSize,
                uint indexTimeStamp)
                : base(moduleService.Services)
            {
                _moduleService = moduleService;
                ModuleIndex = moduleIndex;
                FileName = imageName;
                ImageBase = imageBase;
                ImageSize = imageSize;
                IndexFileSize = indexTimeStamp == InvalidTimeStamp ? null : indexFileSize;
                IndexTimeStamp = indexTimeStamp == InvalidTimeStamp ? null : indexTimeStamp;

                _serviceContainer.AddService<IModuleSymbols>(this);
            }

            public override void Dispose()
            {
                _serviceContainer.RemoveService(typeof(IModuleSymbols));
                base.Dispose();
            }

            #region IModule

            public override Version GetVersionData()
            {
                if (InitializeValue(Module.Flags.InitializeVersion))
                {
                    HResult hr = _moduleService._debuggerServices.GetModuleVersionInformation(ModuleIndex, out VS_FIXEDFILEINFO fileInfo);
                    if (hr.IsOK)
                    {
                        int major = (int)(fileInfo.dwFileVersionMS >> 16);
                        int minor = (int)(fileInfo.dwFileVersionMS & 0xffff);
                        int build = (int)(fileInfo.dwFileVersionLS >> 16);
                        int revision = (int)(fileInfo.dwFileVersionLS & 0xffff);
                        _version = new Version(major, minor, build, revision);
                    }
                    else
                    {
                        _version = GetVersionInner();
                    }
                }
                return _version;
            }

            public override string GetVersionString()
            {
                if (InitializeValue(Module.Flags.InitializeProductVersion))
                {
                    HResult hr = _moduleService._debuggerServices.GetModuleVersionString(ModuleIndex, out _versionString);
                    if (!hr.IsOK)
                    {
                        _versionString = GetVersionStringInner();
                    }
                }
                return _versionString;
            }

            public override string LoadSymbols()
            {
                string symbolFile = _moduleService.SymbolService.DownloadSymbolFile(this);
                if (symbolFile is not null)
                {
                    _moduleService._debuggerServices.AddModuleSymbol(symbolFile);
                }
                return symbolFile;
            }

            #endregion

            #region IModuleSymbols

            bool IModuleSymbols.TryGetSymbolName(ulong address, out string symbol, out ulong displacement)
            {
                return _moduleService._debuggerServices.GetSymbolByOffset(ModuleIndex, address, out symbol, out displacement).IsOK;
            }

            bool IModuleSymbols.TryGetSymbolAddress(string name, out ulong address)
            {
                return _moduleService._debuggerServices.GetOffsetBySymbol(ModuleIndex, name, out address).IsOK;
            }

            bool IModuleSymbols.TryGetType(string typeName, out IType type)
            {
                HResult hr = _moduleService._debuggerServices.GetTypeId(ModuleIndex, typeName, out ulong typeId);
                if (hr != HResult.S_OK)
                {
                    type = null;
                    return false;
                }
                type = new TypeFromDebuggerServices(_moduleService, this, typeId, typeName);
                return true;
            }

            SymbolStatus IModuleSymbols.GetSymbolStatus()
            {
                if (_symbolStatus != SymbolStatus.Unknown)
                {
                    return _symbolStatus;
                }

                // GetSymbolStatus is not implemented for anything other than DbgEng for now.
                IDebugClient client = _moduleService._debuggerServices.DebugClient;
                if (client is null || client is not IDebugSymbols5 symbols)
                {
                    return SymbolStatus.Unknown;
                }

                return _symbolStatus = GetSymbolStatusFromDbgEng(symbols);
            }
            #endregion

            protected override bool TryGetSymbolAddressInner(string name, out ulong address)
            {
                return _moduleService._debuggerServices.GetOffsetBySymbol(ModuleIndex, name, out address).IsOK;
            }

            protected override ModuleService ModuleService => _moduleService;

            private SymbolStatus GetSymbolStatusFromDbgEng(IDebugSymbols5 symbols)
            {
                // First, see if the symbol is already loaded.  Note that getting the symbol type
                // from DbgEng won't force a symbol load, it will only tell us if it's already
                // been loaded or not.
                DEBUG_SYMTYPE symType = GetSymType(symbols, ImageBase);
                if (symType is not DEBUG_SYMTYPE.NONE and not DEBUG_SYMTYPE.DEFERRED)
                {
                    return DebugToSymbolStatus(symType);
                }

                // At this point, the symbol type is DEFERRED or NONE and we haven't tried reloading
                // the symbol yet.  Try a reload, and then ask one last time what the symbol is.
                if (!string.IsNullOrWhiteSpace(FileName))
                {
                    string module = Path.GetFileName(FileName);
                    module = module.Replace('+', '_'); // Reload doesn't like '+' in module names
                    HResult hr = symbols.Reload(module);
                    if (!hr)
                    {
                        // Ugh, Reload might not like the module name that GetModuleName gives us.
                        // Instead, force DbgEng to look up the base address as a symbol which will
                        // force symbol load as well.
                        symbols.GetNameByOffset(ImageBase, null, 0, out _, out _);
                    }
                }

                // Whether we successfully reloaded or not, get the final symbol type.
                symType = GetSymType(symbols, ImageBase);
                return DebugToSymbolStatus(symType);
            }

            private static SymbolStatus DebugToSymbolStatus(DEBUG_SYMTYPE symType)
            {
                // By the time we get here, we've already tried forcing a symbol load.
                // If it's NONE or DEFERRED at this point then we can't load it.  We
                // will never return SymbolStatus.Unknown, so GetSymbolStatusFromDbgEng
                // will only ever be called once per module.
                return symType switch
                {
                    DEBUG_SYMTYPE.NONE => SymbolStatus.NotLoaded,
                    DEBUG_SYMTYPE.DEFERRED => SymbolStatus.NotLoaded,
                    DEBUG_SYMTYPE.EXPORT => SymbolStatus.ExportOnly,
                    _ => SymbolStatus.Loaded,
                };
            }

            private static DEBUG_SYMTYPE GetSymType(IDebugSymbols symbols, ulong imageBase)
            {
                DEBUG_MODULE_PARAMETERS[] moduleParams = new DEBUG_MODULE_PARAMETERS[1];
                HResult hr = symbols.GetModuleParameters(1, new ulong[] { imageBase }, 0, moduleParams);

                DEBUG_SYMTYPE symType = hr ? moduleParams[0].SymbolType : DEBUG_SYMTYPE.NONE;
                return symType;
            }
        }

        private readonly DebuggerServices _debuggerServices;

        internal ModuleServiceFromDebuggerServices(IServiceProvider services, DebuggerServices debuggerServices)
            : base(services)
        {
            Debug.Assert(debuggerServices != null);
            _debuggerServices = debuggerServices;
        }

        /// <summary>
        /// Get/create the modules dictionary.
        /// </summary>
        protected override Dictionary<ulong, IModule> GetModulesInner()
        {
            var modules = new Dictionary<ulong, IModule>();

            HResult hr = _debuggerServices.GetNumberModules(out uint loadedModules, out uint _);
            if (hr.IsOK)
            {
                for (int moduleIndex = 0; moduleIndex < loadedModules; moduleIndex++)
                {
                    hr = _debuggerServices.GetModuleInfo(moduleIndex, out ulong imageBase, out ulong imageSize, out uint timestamp, out uint _);
                    if (hr.IsOK)
                    {
                        hr = _debuggerServices.GetModuleName(moduleIndex, out string imageName);
                        if (hr < HResult.S_OK)
                        {
                            Trace.TraceError("GetModuleName({0}) {1:X16} FAILED {2:X8}", moduleIndex, imageBase, hr);
                        }
                        var module = new ModuleFromDebuggerServices(this, moduleIndex, imageName, imageBase, imageSize, unchecked((uint)imageSize), timestamp);
                        if (!modules.TryGetValue(imageBase, out IModule original))
                        {
                            modules.Add(imageBase, module);
                        }
                        else
                        {
                            Trace.TraceError("Duplicate imageBase {0:X16} new {1} original {2}", imageBase, imageName, original.FileName);
                        }
                    }
                    else
                    {
                        Trace.TraceError("GetModuleInfo({0}) FAILED {1:X8}", moduleIndex, hr);
                    }
                }
            }
            else
            {
                Trace.TraceError("GetNumberModules() FAILED {0:X8}", hr);
            }
            return modules;
        }
    }
}
