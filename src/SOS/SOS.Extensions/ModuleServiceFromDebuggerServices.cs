// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SOS.Extensions
{
    /// <summary>
    /// Module service implementation for the native debugger services
    /// </summary>
    internal class ModuleServiceFromDebuggerServices : ModuleService
    {
        class FieldFromDebuggerServices : IField
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

        class TypeFromDebuggerServices : IType
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

        class ModuleFromDebuggerServices : Module, IModuleSymbols
        {
            // This is what dbgeng/IDebuggerServices returns for non-PE modules that don't have a timestamp
            private const uint InvalidTimeStamp = 0xFFFFFFFE;

            private readonly ModuleServiceFromDebuggerServices _moduleService;
            private Version _version;
            private string _versionString;

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

                ServiceContainer.AddService<IModuleSymbols>(this);
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

            #endregion

            protected override bool TryGetSymbolAddressInner(string name, out ulong address)
            {
                return _moduleService._debuggerServices.GetOffsetBySymbol(ModuleIndex, name, out address).IsOK;
            }

            protected override ModuleService ModuleService => _moduleService;
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

            HResult hr = _debuggerServices.GetNumberModules(out uint loadedModules, out uint unloadedModules);
            if (hr.IsOK)
            {
                for (int moduleIndex = 0; moduleIndex < loadedModules; moduleIndex++)
                {
                    hr = _debuggerServices.GetModuleInfo(moduleIndex, out ulong imageBase, out ulong imageSize, out uint timestamp, out uint checksum);
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
