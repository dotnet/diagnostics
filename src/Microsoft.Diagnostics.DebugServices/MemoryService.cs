// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using SOS;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Memory service CLRMD implementation
    /// </summary>
    public class MemoryService
    {
        private readonly IDataReader _dataReader;
        private readonly MemoryCache _memoryCache;
        private readonly Dictionary<string, PEReader> _pathToPeReader = new Dictionary<string, PEReader>();

        /// <summary>
        /// Memory service constructor
        /// </summary>
        /// <param name="dataReader">CLRMD data reader</param>
        public MemoryService(IDataReader dataReader)
        {
            _dataReader = dataReader;
            _memoryCache = new MemoryCache(ReadMemoryFromModule);
        }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to write to</param>
        /// <param name="bytesRequested">The number of bytes to read</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        public bool ReadMemory(ulong address, byte[] buffer, int bytesRequested, out int bytesRead)
        {
            return ReadMemory(address, new Span<byte>(buffer, 0, bytesRequested), out bytesRead);
        }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to read memory into</param>
        /// <param name="bytesRequested">The number of bytes to read</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        public bool ReadMemory(ulong address, IntPtr buffer, int bytesRequested, out int bytesRead)
        {
            unsafe
            {
                return ReadMemory(address, new Span<byte>(buffer.ToPointer(), bytesRequested), out bytesRead);
            }
        }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to read memory into</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        public bool ReadMemory(ulong address, Span<byte> buffer, out int bytesRead)
        {
            int bytesRequested = buffer.Length;
            bool result = false;
            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    result = _dataReader.ReadMemory(address, new IntPtr(ptr), bytesRequested, out bytesRead);
                }
            }
            // If the read failed or a successful partial read
            if (!result || (bytesRequested != bytesRead))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Check if the memory is in a module and cache it if it is
                    if (_memoryCache.ReadMemory(address + (uint)bytesRead, buffer.Slice(bytesRead), out int read))
                    {
                        bytesRead += read;
                        result = true;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Read memory from a PE module for the memory cache. Finds locally or downloads a module 
        /// and "maps" it into the address space. This function can return more than requested which 
        /// means the block should not be cached.
        /// </summary>
        /// <param name="address">memory address</param>
        /// <param name="bytesRequested">number of bytes</param>
        /// <returns>bytes read or null if error</returns>
        private byte[] ReadMemoryFromModule(ulong address, int bytesRequested)
        {
            // Check if there is a module that contains the address range being read and map it into the virtual address space.
            foreach (ModuleInfo module in _dataReader.EnumerateModules())
            {
                ulong start = module.ImageBase;
                ulong end = start + module.FileSize;
                if (address >= start && address < end)
                {
                    Trace.TraceInformation("ReadMemory: address {0:X16} size {1:X8} found module {2}", address, bytesRequested, module.FileName);

                    // We found a module that contains the memory requested. Now find or download the PE image.
                    PEReader reader = GetPEReader(module);
                    if (reader != null)
                    {
                        // Read the memory from the PE image.
                        int rva = unchecked((int)(address - start));
                        try
                        {
                            byte[] data = null;

                            int sizeOfHeaders = reader.PEHeaders.PEHeader.SizeOfHeaders;
                            if (rva >= 0 && rva < sizeOfHeaders)
                            {
                                // If the address isn't contained in one of the sections, assume that SOS is reader the PE headers directly.
                                Trace.TraceInformation("ReadMemory: rva {0:X8} size {1:X8} in PE Header", rva, bytesRequested);
                                data = reader.GetEntireImage().GetReader(rva, bytesRequested).ReadBytes(bytesRequested);
                            }
                            else
                            {
                                PEMemoryBlock block = reader.GetSectionData(rva);
                                if (block.Length > 0)
                                {
                                    int size = Math.Min(block.Length, bytesRequested);
                                    data = block.GetReader().ReadBytes(size);
                                    ApplyRelocations(module, reader, rva, data);
                                }
                            }

                            return data;
                        }
                        catch (Exception ex) when (ex is BadImageFormatException || ex is InvalidOperationException || ex is IOException)
                        {
                            Trace.TraceError("ReadMemory: exception {0}", ex);
                        }
                    }
                    break;
                }
            }
            return null;
        }

        private PEReader GetPEReader(ModuleInfo module)
        {
            if (!_pathToPeReader.TryGetValue(module.FileName, out PEReader reader))
            {
                Stream stream = null;

                string downloadFilePath = module.FileName;
                if (!File.Exists(downloadFilePath))
                {
                    if (SymbolReader.IsSymbolStoreEnabled())
                    {
                        SymbolStoreKey key = PEFileKeyGenerator.GetKey(Path.GetFileName(downloadFilePath), module.TimeStamp, module.FileSize);
                        if (key != null)
                        {
                            // Now download the module from the symbol server
                            downloadFilePath = SymbolReader.GetSymbolFile(key);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(downloadFilePath))
                {
                    Trace.TraceInformation("GetPEReader: downloading {0}", downloadFilePath);
                    try
                    {
                        stream = File.OpenRead(downloadFilePath);
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException || ex is FileNotFoundException || ex is UnauthorizedAccessException || ex is IOException)
                    {
                        Trace.TraceError("GetPEReader: exception {0}", ex);
                    }
                    if (stream != null)
                    {
                        reader = new PEReader(stream);
                        if (reader.PEHeaders == null || reader.PEHeaders.PEHeader == null) {
                            reader = null;
                        }
                        _pathToPeReader.Add(module.FileName, reader);
                    }
                }
            }
            return reader;
        }

        private void ApplyRelocations(ModuleInfo module, PEReader reader, int dataVA, byte[] data)
        {
            PEMemoryBlock relocations = reader.GetSectionData(".reloc");
            if (relocations.Length > 0)
            {
                ulong baseDelta = module.ImageBase - reader.PEHeaders.PEHeader.ImageBase;
                Trace.TraceInformation("ApplyRelocations: dataVA {0:X8} dataCB {1} baseDelta: {2:X16}", dataVA, data.Length, baseDelta);

                BlobReader blob = relocations.GetReader();
                while (blob.RemainingBytes > 0)
                {
                    // Read IMAGE_BASE_RELOCATION struct
                    int virtualAddress = blob.ReadInt32();
                    int sizeOfBlock = blob.ReadInt32();

                    // Each relocation block covers 4K
                    if (dataVA >= virtualAddress && dataVA < (virtualAddress + 4096))
                    {
                        int entryCount = (sizeOfBlock - 8) / 2;     // (sizeOfBlock - sizeof(IMAGE_BASE_RELOCATION)) / sizeof(WORD)
                        Trace.TraceInformation("ApplyRelocations: reloc VirtualAddress {0:X8} SizeOfBlock {1:X8} entry count {2}", virtualAddress, sizeOfBlock, entryCount);

                        int relocsApplied = 0;
                        for (int e = 0; e < entryCount; e++)
                        {
                            // Read relocation type/offset
                            ushort entry = blob.ReadUInt16();
                            if (entry == 0) {
                                break;
                            }
                            var type = (BaseRelocationType)(entry >> 12);       // type is 4 upper bits
                            int relocVA = virtualAddress + (entry & 0xfff);     // offset is 12 lower bits

                            // Is this relocation in the data?
                            if (relocVA >= dataVA && relocVA < (dataVA + data.Length))
                            {
                                int offset = relocVA - dataVA;
                                switch (type)
                                {
                                    case BaseRelocationType.ImageRelBasedAbsolute:
                                        break;

                                    case BaseRelocationType.ImageRelBasedHighLow:
                                    {
                                        uint value = BitConverter.ToUInt32(data, offset);
                                        value += (uint)baseDelta;
                                        byte[] source = BitConverter.GetBytes(value);
                                        Array.Copy(source, 0, data, offset, source.Length);
                                        break;
                                    }
                                    case BaseRelocationType.ImageRelBasedDir64:
                                    {
                                        ulong value = BitConverter.ToUInt64(data, offset);
                                        value += baseDelta;
                                        byte[] source = BitConverter.GetBytes(value);
                                        Array.Copy(source, 0, data, offset, source.Length);
                                        break;
                                    }
                                    default:
                                        Debug.Fail($"ApplyRelocations: invalid relocation type {type}");
                                        break;
                                }
                                relocsApplied++;
                            }
                        }
                        Trace.TraceInformation("ApplyRelocations: relocs {0} applied", relocsApplied);
                    }
                    else
                    {
                        // Skip to the next relocation block
                        blob.Offset += sizeOfBlock - 8;
                    }
                }
            }
        }

        enum BaseRelocationType
        {
            ImageRelBasedAbsolute   = 0,
            ImageRelBasedHigh       = 1,
            ImageRelBasedLow        = 2,
            ImageRelBasedHighLow    = 3,
            ImageRelBasedHighAdj    = 4,
            ImageRelBasedDir64      = 10,
        }
    }
}
