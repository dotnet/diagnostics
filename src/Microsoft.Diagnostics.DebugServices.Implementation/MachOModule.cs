// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
        // MachO header flag indicating the dylib is part of the dyld shared cache (macOS 11+).
        private const uint MH_DYLIB_IN_CACHE = 0x80000000;

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
            // Skip modules that have no build id, are not macOS, or are part of the dyld shared cache.
            // Since macOS 11 (Big Sur), system libraries are in the shared cache and cannot be downloaded
            // as individual files from symbol servers. The MachOFile service reads the in-memory header
            // (already cached from BuildId resolution) to check the MH_DYLIB_IN_CACHE flag.
            if (module.Target.OperatingSystem == OSPlatform.OSX &&
                !module.BuildId.IsDefaultOrEmpty &&
                !IsDyldSharedCacheModule(module))
            {
                return new MachOModule(module, symbolService);
            }
            return null;
        }

        private static bool IsDyldSharedCacheModule(IModule module)
        {
            MachOFile machOFile = module.Services.GetService<MachOFile>();
            return machOFile is not null && (machOFile.Header.Flags & MH_DYLIB_IN_CACHE) != 0;
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
