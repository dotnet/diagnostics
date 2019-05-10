// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace SOS
{
    public class MetadataHelper
    {
        const int S_OK = 0;
        const int E_FAIL = unchecked((int)0x80004005);

        // HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER) 
        const int E_INSUFFICIENT_BUFFER = unchecked((int)0x8007007a);

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
            [MarshalAs(UnmanagedType.LPWStr)] string imagePath,
            uint imageTimestamp,
            uint imageSize,
            [MarshalAs(UnmanagedType.LPArray, SizeConst = 16)] byte[] mvid,
            uint mdRva,
            uint flags,
            uint bufferSize,
            IntPtr pMetadata,
            IntPtr pMetadataSize)
        {
            int hr = S_OK;
            int dataSize = 0;

            Debug.Assert(pMetadata != IntPtr.Zero);
            try
            {
                Stream peStream = null;
                if (imagePath != null && File.Exists(imagePath))
                {
                    peStream = SymbolReader.TryOpenFile(imagePath);
                }
                else if (SymbolReader.IsSymbolStoreEnabled())
                {
                    SymbolStoreKey key = PEFileKeyGenerator.GetKey(imagePath, imageTimestamp, imageSize);
                    peStream = SymbolReader.GetSymbolStoreFile(key)?.Stream;
                }
                if (peStream != null)
                {
                    using (var peReader = new PEReader(peStream, PEStreamOptions.Default))
                    {
                        if (peReader.HasMetadata)
                        {
                            PEMemoryBlock metadataInfo = peReader.GetMetadata();
                            dataSize = metadataInfo.Length;
                            unsafe
                            {
                                int size = Math.Min((int)bufferSize, metadataInfo.Length);
                                Marshal.Copy(metadataInfo.GetContent().ToArray(), 0, pMetadata, size);
                            }
                        }
                        else
                        {
                            hr = E_FAIL;
                        }
                    }
                }
                else
                {
                    hr = E_FAIL;
                }
            }
            catch (Exception ex) when 
                (ex is UnauthorizedAccessException || 
                 ex is BadImageFormatException || 
                 ex is InvalidVirtualAddressException || 
                 ex is IOException)
            {
                hr = E_FAIL;
            }
            if (pMetadataSize != IntPtr.Zero)
            {
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
        /// <param name="localFilePath">local file path of the module</param>
        /// <returns>HRESULT</returns>
        public static int GetICorDebugMetadataLocator(
            [MarshalAs(UnmanagedType.LPWStr)] string imagePath,
            uint imageTimestamp,
            uint imageSize,
            uint pathBufferSize,
            IntPtr pPathBufferSize,
            IntPtr pPathBuffer)
        {
            int hr = S_OK;
            int actualSize = 0;

            Debug.Assert(pPathBuffer != IntPtr.Zero);
            try
            {
                if (SymbolReader.IsSymbolStoreEnabled())
                {
                    SymbolStoreKey key = PEFileKeyGenerator.GetKey(imagePath, imageTimestamp, imageSize);
                    string localFilePath = SymbolReader.GetSymbolFile(key);
                    localFilePath += "\0";              // null terminate the string
                    actualSize = localFilePath.Length;

                    if (pathBufferSize > actualSize)
                    {
                        Marshal.Copy(localFilePath.ToCharArray(), 0, pPathBuffer, actualSize);
                    }
                    else
                    {
                        hr = E_INSUFFICIENT_BUFFER;
                    }
                }
                else
                {
                    hr = E_FAIL;
                }
            }
            catch (Exception ex) when
                (ex is UnauthorizedAccessException ||
                 ex is BadImageFormatException ||
                 ex is InvalidVirtualAddressException ||
                 ex is IOException)
            {
                hr = E_FAIL;
            }
            
            if (pPathBufferSize != IntPtr.Zero)
            {
                Marshal.WriteInt32(pPathBufferSize, actualSize);
            }

            return hr;
        }
    }
}
