// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.FileFormats;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;

namespace SOS.Hosting
{
    public static class SymbolServiceExtensions
    {
        // HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER) 
        const int E_INSUFFICIENT_BUFFER = unchecked((int)0x8007007a);

        /// <summary>
        /// Set the windows symbol path converting the default "srv*" to the cached public symbol server URL.
        /// </summary>
        /// <param name="symbolPath">The windows symbol path to translate and set</param>
        /// <returns>if false, error parsing symbol path</returns>
        public static bool ParseSymbolPathFixDefault(
            this ISymbolService symbolService,
            string symbolPath)
        {
            // Translate dbgeng's default .sympath to what the public version actually does. Normally "srv*" 
            // means no caching and the server path depends on whether dbgeng is internal or public.
            if (symbolPath.ToLowerInvariant() == "srv*")
            {
                symbolPath = "cache*;SRV*https://msdl.microsoft.com/download/symbols";
            }
            return symbolService.ParseSymbolPath(symbolPath);
        }

        /// <summary>
        /// Metadata locator helper for the DAC.
        /// </summary>
        /// <param name="imagePath">file name and path to module</param>
        /// <param name="imageTimestamp">module timestamp</param>
        /// <param name="imageSize">module image</param>
        /// <param name="mvid">not used</param>
        /// <param name="mdRva">not used</param>
        /// <param name="flags">not used</param>
        /// <param name="bufferSize">size of incoming buffer (pMetadata)</param>
        /// <param name="pMetadata">pointer to buffer</param>
        /// <param name="pMetadataSize">size of outgoing metadata</param>
        /// <returns>HRESULT</returns>
        public static int GetMetadataLocator(
            this ISymbolService symbolService,
            string imagePath,
            uint imageTimestamp,
            uint imageSize,
            byte[] mvid,
            uint mdRva,
            uint flags,
            uint bufferSize,
            IntPtr pMetadata,
            IntPtr pMetadataSize)
        {
            Debug.Assert(imageTimestamp != 0);
            Debug.Assert(imageSize != 0);

            if (pMetadata == IntPtr.Zero) {
                return HResult.E_INVALIDARG;
            }
            int hr = HResult.S_OK;
            int dataSize = 0;

            ImmutableArray<byte> metadata = symbolService.GetMetadata(imagePath, imageTimestamp, imageSize);
            if (!metadata.IsEmpty)
            {
                dataSize = metadata.Length;
                int size = Math.Min((int)bufferSize, dataSize);
                Marshal.Copy(metadata.ToArray(), 0, pMetadata, size);
            }
            else
            {
                hr = HResult.E_FAIL;
            }

            if (pMetadataSize != IntPtr.Zero) {
                Marshal.WriteInt32(pMetadataSize, dataSize);
            }
            return hr;
        }

        /// <summary>
        /// Metadata locator helper for the DAC.
        /// </summary>
        /// <param name="imagePath">file name and path to module</param>
        /// <param name="imageTimestamp">module timestamp</param>
        /// <param name="imageSize">module image</param>
        /// <param name="pathBufferSize">output buffer size</param>
        /// <param name="pPathBufferSize">native pointer to put actual path size</param>
        /// <param name="pwszPathBuffer">native pointer to WCHAR path buffer</param>
        /// <returns>HRESULT</returns>
        public static int GetICorDebugMetadataLocator(
            this ISymbolService symbolService,
            string imagePath,
            uint imageTimestamp,
            uint imageSize,
            uint pathBufferSize,
            IntPtr pPathBufferSize,
            IntPtr pwszPathBuffer)
        {
            int hr = HResult.S_OK;
            int actualSize = 0;

            Debug.Assert(pwszPathBuffer != IntPtr.Zero);
            try
            {
                if (symbolService.IsSymbolStoreEnabled)
                {
                    SymbolStoreKey key = PEFileKeyGenerator.GetKey(imagePath, imageTimestamp, imageSize);
                    string localFilePath = symbolService.DownloadFile(key);
                    if (!string.IsNullOrWhiteSpace(localFilePath))
                    {
                        localFilePath += "\0";              // null terminate the string
                        actualSize = localFilePath.Length;

                        if (pathBufferSize > actualSize)
                        {
                            Trace.TraceInformation($"GetICorDebugMetadataLocator: SUCCEEDED {localFilePath}");
                            Marshal.Copy(localFilePath.ToCharArray(), 0, pwszPathBuffer, actualSize);
                        }
                        else
                        {
                            Trace.TraceError("GetICorDebugMetadataLocator: E_INSUFFICIENT_BUFFER");
                            hr = E_INSUFFICIENT_BUFFER;
                        }
                    }
                    else 
                    {
                        Trace.TraceError($"GetICorDebugMetadataLocator: {imagePath} {imageTimestamp:X8} {imageSize:X8} download FAILED");
                        hr = HResult.E_FAIL;
                    }
                }
                else
                {
                    Trace.TraceError($"GetICorDebugMetadataLocator: {imagePath} {imageTimestamp:X8} {imageSize:X8} symbol store not enabled");
                    hr = HResult.E_FAIL;
                }
            }
            catch (Exception ex) when
                (ex is UnauthorizedAccessException ||
                 ex is BadImageFormatException ||
                 ex is InvalidVirtualAddressException ||
                 ex is IOException)
            {
                Trace.TraceError($"GetICorDebugMetadataLocator: {imagePath} {imageTimestamp:X8} {imageSize:X8} ERROR {ex.Message}");
                hr = HResult.E_FAIL;
            }
            if (pPathBufferSize != IntPtr.Zero)
            {
                Marshal.WriteInt32(pPathBufferSize, actualSize);
            }
            return hr;
        }
    }
}
