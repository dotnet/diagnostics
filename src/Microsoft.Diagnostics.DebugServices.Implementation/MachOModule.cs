// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using Microsoft.FileFormats.MachO;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// MachOModule service that provides downloaded module MachOFile wrapper.
    /// </summary>
    public class MachOModule : IDisposable
    {
        private readonly IModule _module;
        private readonly ISymbolService _symbolService;
        private readonly IDisposable _onChangeEvent;
        private MachOFile _machOFile;

        /// <summary>
        /// Creates a MachOModule service instance of the downloaded or local (if exists) module file.
        /// </summary>
        [ServiceExport(Scope = ServiceScope.Module)]
        public static MachOModule CreateMachOModule(ISymbolService symbolService, IModule module)
        {
            if (module.Target.OperatingSystem == OSPlatform.OSX)
            {
                if (!module.BuildId.IsDefaultOrEmpty)
                {
                    return new MachOModule(module, symbolService);
                }
            }
            return null;
        }

        private MachOModule(IModule module, ISymbolService symbolService)
        {
            _module = module;
            _symbolService = symbolService;
            _onChangeEvent = symbolService.OnChangeEvent.Register(() => {
                _machOFile?.Dispose();
                _machOFile = null;
            });
        }

        public MachOFile GetMachOFile()
        {
            _machOFile ??= Utilities.OpenMachOFile(_symbolService.DownloadModuleFile(_module));
            return _machOFile;
        }

        public void Dispose()
        {
            _machOFile?.Dispose();
            _onChangeEvent.Dispose();
        }
    }
}
