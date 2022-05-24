// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.FileFormats.MachO;
using System;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Disposable MachOFile wrapper around the module file.
    /// </summary>
    public class MachOModule : MachOFile, IDisposable
    {
        private readonly Stream _stream;

        /// <summary>
        /// Opens and returns an MachOFile instance from the local file path
        /// </summary>
        /// <param name="filePath">MachO file to open</param>
        /// <returns>MachOFile instance or null</returns>
        public static MachOModule OpenFile(string filePath)
        {
            Stream stream = Utilities.TryOpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    var machoModule = new MachOModule(stream);
                    if (!machoModule.IsValid())
                    {
                        Trace.TraceError($"OpenMachOFile: not a valid file");
                        return null;
                    }
                    return machoModule;
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenMachOFile: exception {ex.Message}");
                }
            }
            return null;
        }

        public MachOModule(Stream stream) :
            base(new StreamAddressSpace(stream), position: 0, dataSourceIsVirtualAddressSpace: false)
        {
            _stream = stream;
        }

        public void Dispose() => _stream.Dispose();
    }
}
