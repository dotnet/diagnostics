// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Memory service wrapper that maps and fixes up PE module on read memory errors.
    /// </summary>
    internal class ImageMappingMemoryService : IMemoryService
    {
        private readonly IMemoryService _memoryService;
        private readonly IModuleService _moduleService;
        private readonly MemoryCache _memoryCache;
        private readonly HashSet<ulong> _recursionProtection;

        /// <summary>
        /// The PE and ELF image mapping memory service. This service assumes that the managed PE 
        /// assemblies are in the module service's list. This is true for dbgeng, dotnet-dump but not
        /// true for lldb (only native modules are provided).
        /// </summary>
        /// <param name="target">target instance</param>
        /// <param name="memoryService">memory service to wrap</param>
        internal ImageMappingMemoryService(ITarget target, IMemoryService memoryService)
        {
            _memoryService = memoryService;
            _moduleService = target.Services.GetService<IModuleService>();
            _memoryCache = new MemoryCache(ReadMemoryFromModule);
            _recursionProtection = new HashSet<ulong>();
            target.OnFlushEvent.Register(_memoryCache.FlushCache);
            target.DisposeOnDestroy(target.Services.GetService<ISymbolService>()?.OnChangeEvent.Register(_memoryCache.FlushCache));
        }

        #region IMemoryService

        /// <summary>
        /// Returns the pointer size of the target
        /// </summary>
        public int PointerSize => _memoryService.PointerSize;

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
            _memoryService.ReadMemory(address, buffer, out bytesRead);

            // If the read failed or a successful partial read
            if (bytesRequested != bytesRead)
            {
                // Check if the memory is in a module and cache it if it is
                if (_memoryCache.ReadMemory(address + (uint)bytesRead, buffer.Slice(bytesRead), out int read))
                {
                    bytesRead += read;
                }
            }
            return bytesRead > 0;
        }

        /// <summary>
        /// Write memory into target process for supported targets.
        /// </summary>
        /// <param name="address">The address of memory to write</param>
        /// <param name="buffer">The buffer to write</param>
        /// <param name="bytesWritten">The number of bytes successfully written</param>
        /// <returns>true if any bytes where written, false if write failed</returns>
        public bool WriteMemory(ulong address, Span<byte> buffer, out int bytesWritten)
        {
            return _memoryService.WriteMemory(address, buffer, out bytesWritten);
        }

        #endregion

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
            Debug.Assert((address & ~_memoryService.SignExtensionMask()) == 0);
            IModule module = _moduleService.GetModuleFromAddress(address);
            if (module != null)
            {
                // Recursion can happen in the case where the PE, ELF or MachO headers (in the module.Services.GetService<>() calls)
                // used to get the timestamp/filesize or build id are not in the dump.
                if (!_recursionProtection.Contains(address))
                {
                    _recursionProtection.Add(address);
                    try
                    {
                        // We found a module that contains the memory requested. Now find or download the PE image.
                        PEReader reader = module.Services.GetService<PEReader>();
                        if (reader is not null)
                        {
                            int rva = (int)(address - module.ImageBase);
                            Debug.Assert(rva >= 0);
                            Debug.Assert(!reader.IsLoadedImage);
                            Debug.Assert(reader.IsEntireImageAvailable);
#if TRACE_VERBOSE
                            Trace.TraceInformation($"ReadMemoryFromModule: address {address:X16} rva {rva:X8} bytesRequested {bytesRequested:X8} {module.FileName}");
#endif
                            // Not reading anything in the PE's header
                            if (rva > reader.PEHeaders.PEHeader.SizeOfHeaders)
                            {
                                // This property can cause recursion because this PE being mapped here is read to determine the layout 
                                if (!module.IsFileLayout.GetValueOrDefault(true))
                                {
                                    // If the PE image that we are mapping into has the "loaded" layout convert the rva to a flat/file based one.
                                    for (int i = 0; i < reader.PEHeaders.SectionHeaders.Length; i++)
                                    {
                                        SectionHeader section = reader.PEHeaders.SectionHeaders[i];
                                        if (rva >= section.VirtualAddress && rva < (section.VirtualAddress + section.VirtualSize))
                                        {
                                            rva = section.PointerToRawData + (rva - section.VirtualAddress);
                                            break;
                                        }
                                    }
                                }
                            }

                            try
                            {
                                byte[] data = null;

                                // Read the memory from the PE image found/downloaded above
                                PEMemoryBlock block = reader.GetEntireImage();
                                if (rva < block.Length)
                                {
                                    int size = Math.Min(block.Length - rva, bytesRequested);
                                    if ((rva + size) <= block.Length)
                                    {
                                        data = block.GetReader(rva, size).ReadBytes(size);
                                        ApplyRelocations(module, reader, rva, data);
                                    }
                                    else
                                    {
                                        Trace.TraceError($"ReadMemoryFromModule: FAILED address {address:X16} rva {rva:X8} {module.FileName}");
                                    }
                                }
                                
                                return data;
                            }
                            catch (Exception ex) when (ex is BadImageFormatException || ex is InvalidOperationException || ex is IOException)
                            {
                                Trace.TraceError($"ReadMemoryFromModule: exception: address {address:X16} {ex.Message} {module.FileName}");
                            }
                        }
                        else
                        {
                            // Find or download the ELF image, if one.
                            Reader virtualAddressReader = module.Services.GetService<ELFFile>()?.VirtualAddressReader;
                            if (virtualAddressReader is null)
                            {
                                // Find or download the MachO image, if one.
                                virtualAddressReader = module.Services.GetService<MachOFile>()?.VirtualAddressReader;
                            }
                            if (virtualAddressReader is not null)
                            {
                                // Read the memory from the image.
                                ulong rva = address - module.ImageBase;
                                Debug.Assert(rva >= 0);
                                try
                                {
                                    Trace.TraceInformation($"ReadMemoryFromModule: address {address:X16} rva {rva:X16} size {bytesRequested:X8} in ELF or MachO file {module.FileName}");
                                    byte[] data = new byte[bytesRequested];
                                    uint read = virtualAddressReader.Read(rva, data, 0, (uint)bytesRequested);
                                    if (read == 0)
                                    {
                                        Trace.TraceError($"ReadMemoryFromModule: FAILED address {address:X16} rva {rva:X16} {module.FileName}");
                                        data = null;
                                    }
                                    return data;
                                }
                                catch (Exception ex) when (ex is BadInputFormatException || ex is InvalidVirtualAddressException)
                                {
                                    Trace.TraceError($"ReadMemoryFromModule: ELF or MachO file exception: address {address:X16} {ex.Message} {module.FileName}");
                                }
                            }
                        }
                    }
                    finally
                    {
                        _recursionProtection.Remove(address);
                    }
                }
                else
                {
                    Trace.TraceError($"ReadMemoryFromModule: recursion: address {address:X16} size {bytesRequested:X8} {module.FileName}");
                }
            }
            return null;
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

        private void ApplyRelocations(IModule module, PEReader reader, int dataVA, byte[] data)
        {
            PEMemoryBlock relocations = reader.GetSectionData(".reloc");
            if (relocations.Length > 0)
            {
                ulong baseDelta = module.ImageBase - reader.PEHeaders.PEHeader.ImageBase;
#if TRACE_VERBOSE
                Trace.TraceInformation("ApplyRelocations: dataVA {0:X8} dataCB {1} baseDelta: {2:X16}", dataVA, data.Length, baseDelta);
#endif
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
#if TRACE_VERBOSE
                        Trace.TraceInformation("ApplyRelocations: reloc VirtualAddress {0:X8} SizeOfBlock {1:X8} entry count {2}", virtualAddress, sizeOfBlock, entryCount);
#endif
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
                                        if ((offset + sizeof(uint)) <= data.Length)
                                        {
                                            uint value = BitConverter.ToUInt32(data, offset);
                                            value += (uint)baseDelta;
                                            byte[] source = BitConverter.GetBytes(value);
                                            Array.Copy(source, 0, data, offset, source.Length);
                                        }
                                        break;
                                    
                                    case BaseRelocationType.ImageRelBasedDir64:
                                        if ((offset + sizeof(ulong)) <= data.Length)
                                        {
                                            ulong value = BitConverter.ToUInt64(data, offset);
                                            value += baseDelta;
                                            byte[] source = BitConverter.GetBytes(value);
                                            Array.Copy(source, 0, data, offset, source.Length);
                                        }
                                        break;
                                    
                                    default:
                                        Debug.Fail($"ApplyRelocations: invalid relocation type {type}");
                                        break;
                                }
                                relocsApplied++;
                            }
                        }
#if TRACE_VERBOSE
                        Trace.TraceInformation("ApplyRelocations: relocs {0} applied", relocsApplied);
#endif
                    }
                    else
                    {
                        // Skip to the next relocation block
                        blob.Offset += sizeOfBlock - 8;
                    }
                }
            }
        }
    }
}
