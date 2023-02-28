// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using Microsoft.FileFormats.ELF;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ELFModule service that provides downloaded module ELFFile wrapper.
    /// </summary>
    public class ELFModule : IDisposable
    {
        private readonly IModule _module;
        private readonly ISymbolService _symbolService;
        private readonly IDisposable _onChangeEvent;
        private ELFFile _elfFile;

        /// <summary>
        /// Creates a ELFModule service instance of the downloaded or local (if exists) module file.
        /// </summary>
        [ServiceExport(Scope = ServiceScope.Module)]
        public static ELFModule CreateELFModule(IModule module, ISymbolService symbolService)
        {
            if (module.Target.OperatingSystem == OSPlatform.Linux)
            {
                if (!module.BuildId.IsDefaultOrEmpty)
                {
                    return new ELFModule(module, symbolService);
                }
            }
            return null;
        }

        private ELFModule(IModule module, ISymbolService symbolService)
        {
            _module = module;
            _symbolService = symbolService;
            _onChangeEvent = symbolService.OnChangeEvent.Register(() => {
                _elfFile?.Dispose();
                _elfFile = null;
            });
        }

        public ELFFile GetELFFile()
        {
            _elfFile ??= Utilities.OpenELFFile(_symbolService.DownloadModuleFile(_module));
            return _elfFile;
        }

        public void Dispose()
        {
            _elfFile?.Dispose();
            _onChangeEvent.Dispose();
        }
    }
}
