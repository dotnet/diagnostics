// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Module service implementation for the clrmd IDataReader
    /// </summary>
    public class ModuleServiceFromDataReader : ModuleService
    {
        class ModuleFromDataReader : Module, IExportSymbols
        {
            // This is what clrmd returns for non-PE modules that don't have a timestamp
            private const uint InvalidTimeStamp = 0;

            private static readonly Microsoft.Diagnostics.Runtime.VersionInfo EmptyVersionInfo = new (0, 0, 0, 0);
            private readonly ModuleServiceFromDataReader _moduleService;
            private readonly IExportReader _exportReader;
            private readonly ModuleInfo _moduleInfo;
            private readonly ulong _imageSize;
            private VersionData _versionData;
            private string _versionString;

            public ModuleFromDataReader(ModuleServiceFromDataReader moduleService, IExportReader exportReader, int moduleIndex, ModuleInfo moduleInfo, ulong imageSize)
                : base(moduleService.Target)
            {
                _moduleService = moduleService;
                _moduleInfo = moduleInfo;
                _imageSize = imageSize;
                _exportReader = exportReader;
                ModuleIndex = moduleIndex;
                if (exportReader is not null)
                {
                    ServiceProvider.AddService<IExportSymbols>(this);
                }
            }

            #region IModule

            public override int ModuleIndex { get; }

            public override string FileName => _moduleInfo.FileName;

            public override ulong ImageBase => _moduleInfo.ImageBase;

            public override ulong ImageSize => _imageSize;

            public override uint? IndexFileSize => _moduleInfo.IndexTimeStamp == InvalidTimeStamp ? null : (uint)_moduleInfo.IndexFileSize;

            public override uint? IndexTimeStamp => _moduleInfo.IndexTimeStamp == InvalidTimeStamp ? null : (uint)_moduleInfo.IndexTimeStamp;

            public override VersionData VersionData
            {
                get 
                {
                    if (InitializeValue(Module.Flags.InitializeVersion))
                    {
                        if (_moduleInfo.Version != EmptyVersionInfo)
                        {
                            _versionData = _moduleInfo.Version.ToVersionData();
                        }
                        else
                        {
                            if (_moduleService.Target.OperatingSystem != OSPlatform.Windows)
                            {
                                _versionData = GetVersion();
                            }
                        }
                    }
                    return _versionData;
                }
            }

            public override string VersionString
            {
                get
                {
                    if (InitializeValue(Module.Flags.InitializeProductVersion))
                    {
                        if (_moduleService.Target.OperatingSystem != OSPlatform.Windows && !IsPEImage)
                        {
                            _versionString = _moduleService.GetVersionString(ImageBase, ImageSize);
                        }
                    }
                    return _versionString;
                }
            }

            #endregion

            #region IExportSymbols

            public bool TryGetSymbolAddress(string name, out ulong address)
            {
                if (_exportReader is not null)
                {
                    // Some exceptions are escaping from the clrmd ELF dump reader. This will be
                    // fixed in a clrmd update.
                    try
                    {
                        return _exportReader.TryGetSymbolAddress(ImageBase, name, out address);
                    }
                    catch (IOException)
                    {
                    }
                }
                address = 0;
                return false;
            }

            #endregion

            protected override ModuleService ModuleService => _moduleService;
        }

        private readonly IDataReader _dataReader;

        public ModuleServiceFromDataReader(ITarget target, IDataReader dataReader)
            : base(target)
        {
            _dataReader = dataReader;
        }

        /// <summary>
        /// Get/create the modules dictionary.
        /// </summary>
        protected override Dictionary<ulong, IModule> GetModulesInner()
        {
            var modules = new Dictionary<ulong, IModule>();
            int moduleIndex = 0;

            IExportReader exportReader = _dataReader as IExportReader;
            ModuleInfo[] moduleInfos = _dataReader.EnumerateModules().OrderBy((info) => info.ImageBase).ToArray();
            for (int i = 0; i < moduleInfos.Length; i++)
            {
                ModuleInfo moduleInfo = moduleInfos[i];
                ulong imageSize = (uint)moduleInfo.IndexFileSize;
                if ((i + 1) < moduleInfos.Length)
                {
                    ModuleInfo moduleInfoNext = moduleInfos[i + 1];
                    ulong start = moduleInfo.ImageBase;
                    ulong end = moduleInfo.ImageBase + imageSize;
                    ulong startNext = moduleInfoNext.ImageBase;

                    if (end > startNext)
                    {
                        Trace.TraceWarning($"Module {moduleInfo.FileName} {start:X16} - {end:X16} ({imageSize:X8})");
                        Trace.TraceWarning($"  overlaps with {moduleInfoNext.FileName} {startNext:X16}");
                        imageSize = startNext - start;
                    }
                }
                var module = new ModuleFromDataReader(this, exportReader, moduleIndex, moduleInfo, imageSize);
                try
                {
                    modules.Add(moduleInfo.ImageBase, module);
                }
                catch (ArgumentException)
                {
                    Trace.TraceError($"GetModules(): duplicate module base '{module}' dup '{modules[moduleInfo.ImageBase]}'");
                }
                moduleIndex++;
            }
            return modules;
        }
    }
}
