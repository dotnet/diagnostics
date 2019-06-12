// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
            int hr = unchecked((int)0x80004005);
            int dataSize = 0;

            Debug.Assert(pMetadata != IntPtr.Zero);

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
                        unsafe
                        {
                            int size = Math.Min((int)bufferSize, metadataInfo.Length);
                            Marshal.Copy(metadataInfo.GetContent().ToArray(), 0, pMetadata, size);
                        }
                        dataSize = metadataInfo.Length;
                        hr = 0;
                    }
                }
            }

            if (pMetadataSize != IntPtr.Zero)
            {
                Marshal.WriteInt32(pMetadataSize, dataSize);
            }
            return hr;
        }
    }
}
