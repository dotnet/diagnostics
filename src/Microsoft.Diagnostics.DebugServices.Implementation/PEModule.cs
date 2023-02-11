// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Reflection.PortableExecutable;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// PEModule service that provides downloaded module PEReader wrapper.
    /// </summary>
    public class PEModule : IDisposable
    {
        private readonly IModule _module;
        private readonly ISymbolService _symbolService;
        private readonly IDisposable _onChangeEvent;
        private PEReader _reader;

        /// <summary>
        /// Creates a PEModule service instance of the downloaded or local (if exists) module file.
        /// </summary>
        [ServiceExport(Scope = ServiceScope.Module)]
        public static PEModule CreatePEModule(IModule module, ISymbolService symbolService)
        {
            if (module.IndexTimeStamp.HasValue && module.IndexFileSize.HasValue)
            {
                return new PEModule(module, symbolService);
            }
            return null;
        }

        public PEReader GetPEReader()
        {
            if (_reader == null)
            {
                _reader = Utilities.OpenPEReader(_symbolService.DownloadModuleFile(_module));
            }
            return _reader;
        }

        private PEModule(IModule module, ISymbolService symbolService)
        {
            _module = module;
            _symbolService = symbolService;
            _onChangeEvent = symbolService.OnChangeEvent.Register(() => {
                _reader?.Dispose();
                _reader = null;
            });
        }

        public void Dispose()
        {
            _reader?.Dispose();
            _onChangeEvent.Dispose();
        }
    }
}