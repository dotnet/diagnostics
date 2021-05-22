// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Disposable ELFFile wrapper around the module file.
    /// </summary>
    public class ELFModule : ELFFile
    {
        private readonly Stream _stream;
        private readonly IDisposable _disposable;

        /// <summary>
        /// Creates a ELFFile service instance of the module in memory.
        /// </summary>
        [ServiceExport(Scope = ServiceScope.Module)]
        public static ELFFile CreateELFFile(IMemoryService memoryService, IModule module)
        {
            if (module.Target.OperatingSystem == OSPlatform.Linux)
            {
                Stream stream = memoryService.CreateMemoryStream();
                var elfFile = new ELFFile(new StreamAddressSpace(stream), module.ImageBase, true);
                if (elfFile.IsValid())
                {
                    return elfFile;
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a ELFModule service instance of the downloaded or local (if exists) module file.
        /// </summary>
        [ServiceExport(Scope = ServiceScope.Module)]
        public static ELFModule CreateELFModule(IServiceContainer container, ISymbolService symbolService, IModule module)
        {
            if (module.Target.OperatingSystem == OSPlatform.Linux)
            {
                if (!module.BuildId.IsDefaultOrEmpty)
                {
                    IDisposable onChangeEvent = symbolService.OnChangeEvent.Register(() => container.RemoveService(typeof(ELFModule)));
                    return OpenFile(symbolService.DownloadModuleFile(module), onChangeEvent);
                }
            }
            return null;
        }

        /// <summary>
        /// Opens and returns an ELFFile instance from the local file path
        /// </summary>
        /// <param name="filePath">ELF file to open</param>
        /// <param name="disposable">optional object to be disposed along with this one</param>
        /// <returns>ELFFile instance or null</returns>
        public static ELFModule OpenFile(string filePath, IDisposable disposable = null)
        {
            Stream stream = Utilities.TryOpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    ELFModule elfModule = new(stream, disposable);
                    if (!elfModule.IsValid())
                    {
                        Trace.TraceError($"OpenELFFile: not a valid file {filePath}");
                        return null;
                    }
                    return elfModule;
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenELFFile: {filePath} exception {ex.Message}");
                }
            }
            return null;
        }

        private ELFModule(Stream stream, IDisposable disposable) :
            base(new StreamAddressSpace(stream), position: 0, isDataSourceVirtualAddressSpace: false)
        {
            _stream = stream;
            _disposable = disposable;
        }

        public void Dispose()
        {
            _stream.Dispose();
            _disposable?.Dispose();
        }
    }
}
