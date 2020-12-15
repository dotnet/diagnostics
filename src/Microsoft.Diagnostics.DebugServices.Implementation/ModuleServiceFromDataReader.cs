// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
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
            private static readonly VersionInfo EmptyVersionInfo = new VersionInfo(0, 0, 0, 0);
            private readonly ModuleServiceFromDataReader _moduleService;
            private readonly ModuleInfo _moduleInfo;
            private string _versionString;

            public ModuleFromDataReader(ModuleServiceFromDataReader moduleService, int moduleIndex, ModuleInfo moduleInfo)
            {
                _moduleService = moduleService;
                _moduleInfo = moduleInfo;
                ModuleIndex = moduleIndex;
            }

            #region IModule

            public override int ModuleIndex { get; }

            public override string FileName => _moduleInfo.FileName;

            public override ulong ImageBase => _moduleInfo.ImageBase;

            public override ulong ImageSize => (uint)_moduleInfo.IndexFileSize;

            public override int IndexFileSize => _moduleInfo.IndexFileSize;

            public override int IndexTimeStamp => _moduleInfo.IndexTimeStamp;

            public override VersionInfo? Version
            {
                get 
                {
                    if (InitializeValue(Module.Flags.InitializeVersion))
                    {
                        if (_moduleInfo.Version != EmptyVersionInfo)
                        {
                            base.Version = _moduleInfo.Version;
                        }
                        else
                        {
                            if (_moduleService.Target.OperatingSystem != OSPlatform.Windows)
                            {
                                GetVersionFromVersionString();
                            }
                        }
                    }
                    return base.Version;
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

            foreach (ModuleInfo moduleInfo in _dataReader.EnumerateModules().OrderBy((info) => info.ImageBase))
            {
                var module = new ModuleFromDataReader(this, moduleIndex, moduleInfo);
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
