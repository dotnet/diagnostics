// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Module service implementation for the clrmd IDataReader
    /// </summary>
    public class ModuleServiceFromDataReader : ModuleService
    {
        class ModuleFromDataReader : Module
        {
            // This is what clrmd returns for non-PE modules that don't have a timestamp
            private const uint InvalidTimeStamp = 0;

            private readonly ModuleServiceFromDataReader _moduleService;
            private readonly ModuleInfo _moduleInfo;
            private readonly ulong _imageSize;
            private Version _version;
            private string _versionString;

            public ModuleFromDataReader(ModuleServiceFromDataReader moduleService, int moduleIndex, ModuleInfo moduleInfo, ulong imageSize)
                : base(moduleService.Target)
            {
                _moduleService = moduleService;
                _moduleInfo = moduleInfo;
                _imageSize = imageSize;
                ModuleIndex = moduleIndex;
            }

            #region IModule

            public override int ModuleIndex { get; }

            public override string FileName => _moduleInfo.FileName;

            public override ulong ImageBase => _moduleInfo.ImageBase;

            public override ulong ImageSize => _imageSize;

            public override uint? IndexFileSize => _moduleInfo.IndexTimeStamp == InvalidTimeStamp ? null : (uint)_moduleInfo.IndexFileSize;

            public override uint? IndexTimeStamp => _moduleInfo.IndexTimeStamp == InvalidTimeStamp ? null : (uint)_moduleInfo.IndexTimeStamp;

            public override ImmutableArray<byte> BuildId
            {
                get
                {
                    if (_buildId.IsDefault)
                    {
                        ImmutableArray<byte> buildId = _moduleInfo.BuildId;
                        // If the data reader can't get the build id, it returns a empty (instead of default) immutable array.
                        _buildId = buildId.IsDefaultOrEmpty ? base.BuildId : buildId;
                    }
                    return _buildId;
                }
            }

            public override Version GetVersionData()
            {
                if (InitializeValue(Module.Flags.InitializeVersion))
                {
                    if (!_moduleInfo.Version.Equals(Utilities.EmptyVersion))
                    {
                        _version = _moduleInfo.Version;
                    }
                    else
                    {
                        if (_moduleService.Target.OperatingSystem != OSPlatform.Windows)
                        {
                            _version = GetVersionInner();
                        }
                    }
                }
                return _version;
            }

            public override string GetVersionString()
            {
                if (InitializeValue(Module.Flags.InitializeProductVersion))
                {
                    if (_moduleService.Target.OperatingSystem != OSPlatform.Windows && !IsPEImage)
                    {
                        _versionString = _moduleService.GetVersionString(this);
                    }
                }
                return _versionString;
            }

            public override string LoadSymbols()
            {
                return _moduleService.SymbolService.DownloadSymbolFile(this);
            }

            #endregion

            protected override bool TryGetSymbolAddressInner(string name, out ulong address)
            {
                address = _moduleInfo.GetExportSymbolAddress(name);
                return address != 0;
            }

            protected override ModuleService ModuleService => _moduleService;
        }

        private readonly IDataReader _dataReader;

        public ModuleServiceFromDataReader(ITarget target, IMemoryService rawMemoryService, IDataReader dataReader)
            : base(target, rawMemoryService)
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

            ModuleInfo[] moduleInfos = _dataReader.EnumerateModules().OrderBy((info) => info.ImageBase).ToArray();
            for (int i = 0; i < moduleInfos.Length; i++)
            {
                ModuleInfo moduleInfo = moduleInfos[i];
                ulong imageSize = (ulong)moduleInfo.ImageSize;

                // Only add images that have a size. On Linux these are special files like /run/shm/lttng-ust-wait-8-1000 and non-ELF
                // resource(?) files like /usr/share/zoneinfo-icu/44/le/metaZones.res. Haven't see any 0 sized PE or MachO files.
                if (imageSize > 0)
                {
                    // There are times when the module infos returned by the data reader overlap which breaks the module
                    // service's address binary search. This code adjusts the module's image size to the next module's
                    // image base address if there is overlap.
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
                    var module = new ModuleFromDataReader(this, moduleIndex, moduleInfo, imageSize);
                    try
                    {
                        modules.Add(moduleInfo.ImageBase, module);
                    }
                    catch (ArgumentException)
                    {
                        Trace.TraceError($"GetModules(): duplicate module base '{module}' dup '{modules[moduleInfo.ImageBase]}'");
                    }
                }

                moduleIndex++;
            }
            return modules;
        }
    }
}
