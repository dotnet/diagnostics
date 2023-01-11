// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using System;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Disposable ELFFile wrapper around the module file.
    /// </summary>
    public class ELFModule : ELFFile
    {
        /// <summary>
        /// Opens and returns an ELFFile instance from the local file path
        /// </summary>
        /// <param name="filePath">ELF file to open</param>
        /// <returns>ELFFile instance or null</returns>
        public static ELFModule OpenFile(string filePath)
        {
            Stream stream = Utilities.TryOpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    ELFModule elfModule = new(stream);
                    if (!elfModule.IsValid())
                    {
                        Trace.TraceError($"OpenELFFile: not a valid file");
                        return null;
                    }
                    return elfModule;
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenELFFile: exception {ex.Message}");
                }
            }
            return null;
        }

        public ELFModule(Stream stream) :
            base(new StreamAddressSpace(stream), position: 0, isDataSourceVirtualAddressSpace: false)
        {
        }
    }
}