// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Disposable PEReader wrapper around the module file.
    /// </summary>
    public class PEModule : IDisposable
    {
        private readonly IDisposable _disposable;

        public PEReader Reader { get; private set; }

        /// <summary>
        /// Creates a PEModule service instance of the downloaded or local (if exists) module file.
        /// </summary>
        [ServiceExport(Scope = ServiceScope.Module)]
        public static PEModule CreatePEModule(IServiceContainer container, ISymbolService symbolService, IModule module)
        {
            if (module.IndexTimeStamp.HasValue && module.IndexFileSize.HasValue)
            {
                IDisposable onChangeEvent = symbolService.OnChangeEvent.Register(() => container.RemoveService(typeof(PEModule)));
                PEReader reader = OpenPEReader(symbolService.DownloadModuleFile(module));
                if (reader is not null)
                {
                    return new PEModule(reader, onChangeEvent);
                }
            }
            return null;
        }

        /// <summary>
        /// Opens and returns an PEReader instance from the local file path
        /// </summary>
        /// <param name="filePath">PE file to open</param>
        /// <returns>PEReader instance or null</returns>
        public static PEReader OpenPEReader(string filePath)
        {
            Stream stream = Utilities.TryOpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    var reader = new PEReader(stream);
                    if (reader.PEHeaders == null || reader.PEHeaders.PEHeader == null)
                    {
                        Trace.TraceWarning($"OpenPEReader: PEReader invalid headers");
                        return null;
                    }
                    return reader;
                }
                catch (Exception ex) when (ex is BadImageFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenPEReader: PEReader exception {ex.Message}");
                }
            }
            return null;
        }

        private PEModule(PEReader reader, IDisposable disposable)
        {
            Reader = reader;
            _disposable = disposable;
        }

        public void Dispose()
        {
            Reader?.Dispose();
            _disposable?.Dispose();
        }
    }
}