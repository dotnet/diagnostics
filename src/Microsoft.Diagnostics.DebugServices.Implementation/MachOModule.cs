// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.FileFormats.MachO;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Disposable MachOFile wrapper around the module file.
    /// </summary>
    public class MachOModule : MachOFile
    {
        private readonly Stream _stream;
        private readonly IDisposable _disposable;

        /// <summary>
        /// Creates a MachOFile service instance of the module in memory.
        /// </summary>
        [ServiceExport(Scope = ServiceScope.Module)]
        public static MachOFile CreateMachOFile(IMemoryService memoryService, IModule module)
        {
            if (module.Target.OperatingSystem == OSPlatform.OSX)
            {
                Stream stream = memoryService.CreateMemoryStream();
                var elfFile = new MachOFile(new StreamAddressSpace(stream), module.ImageBase, true);
                if (elfFile.IsValid())
                {
                    return elfFile;
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a MachOModule service instance of the downloaded or local (if exists) module file.
        /// </summary>
        [ServiceExport(Scope = ServiceScope.Module)]
        public static MachOModule CreateMachOModule(IServiceContainer container, ISymbolService symbolService, IModule module)
        {
            if (module.Target.OperatingSystem == OSPlatform.OSX)
            {
                if (!module.BuildId.IsDefaultOrEmpty)
                {
                    IDisposable onChangeEvent = symbolService.OnChangeEvent.Register(() => container.RemoveService(typeof(MachOModule)));
                    return OpenFile(symbolService.DownloadModuleFile(module), onChangeEvent);
                }
            }
            return null;
        }

        /// <summary>
        /// Opens and returns an MachOFile instance from the local file path
        /// </summary>
        /// <param name="filePath">MachO file to open</param>
        /// <param name="disposable">optional object to be disposed along with this one</param>
        /// <returns>MachOFile instance or null</returns>
        public static MachOModule OpenFile(string filePath, IDisposable disposable = null)
        {
            Stream stream = Utilities.TryOpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    var machoModule = new MachOModule(stream, disposable);
                    if (!machoModule.IsValid())
                    {
                        Trace.TraceError($"OpenMachOFile: not a valid file {filePath}");
                        return null;
                    }
                    return machoModule;
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenMachOFile: {filePath} exception {ex.Message}");
                }
            }
            return null;
        }

        private MachOModule(Stream stream, IDisposable disposable) :
            base(new StreamAddressSpace(stream), position: 0, dataSourceIsVirtualAddressSpace: false)
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
