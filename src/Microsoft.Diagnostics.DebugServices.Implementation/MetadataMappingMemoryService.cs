// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System.Linq;
using System;
using System.Collections.Immutable;
using System.IO;
using Microsoft.Diagnostics.Runtime.Utilities;
using System.Diagnostics;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Memory service wrapper that maps always module's metadata into the address 
    /// space even is some or all of the memory exists in the coredump. lldb returns
    /// zero's (instead of failing the memory read) for missing pages in core dumps 
    /// that older (less than 5.0) createdumps generate  so it needs this special 
    /// metadata  mapping memory service.
    /// </summary>
    internal class MetadataMappingMemoryService : IMemoryService
    {
        private readonly IRuntime _runtime;
        private readonly IMemoryService _memoryService;
        private readonly ISymbolService _symbolService;
        private bool _regionInitialized;
        private ImmutableArray<MetadataRegion> _regions;

        /// <summary>
        /// Memory service constructor
        /// </summary>
        /// <param name="runtime">runtime instance</param>
        /// <param name="memoryService">memory service to wrap</param>
        /// <param name="symbolService">symbol service</param>
        internal MetadataMappingMemoryService(IRuntime runtime, IMemoryService memoryService, ISymbolService symbolService)
        {
            _runtime = runtime;
            _memoryService = memoryService;
            _symbolService = symbolService;
        }

        /// <summary>
        /// Flush the metadata memory service
        /// </summary>
        public void Flush()
        {
            _regionInitialized = false;
            _regions.Clear();
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
            Debug.Assert((address & ~_memoryService.SignExtensionMask()) == 0);
            if (buffer.Length > 0)
            {
                MetadataRegion region = FindRegion(address);
                if (region != null)
                {
                    if (region.ReadMetaData(address, buffer, out bytesRead)) {
                        return true;
                    }
                }
            }
            return _memoryService.ReadMemory(address, buffer, out bytesRead);
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
            Debug.Assert((address & ~_memoryService.SignExtensionMask()) == 0);
            return _memoryService.WriteMemory(address, buffer, out bytesWritten);
        }

        #endregion

        private MetadataRegion FindRegion(ulong address)
        {
            if (!_regionInitialized)
            {
                _regionInitialized = true;

                Trace.TraceInformation($"FindRegion: initializing regions for runtime #{_runtime.Id}");
                ClrRuntime clrruntime = _runtime.Services.GetService<ClrRuntime>();
                if (clrruntime != null)
                {
                    _regions = clrruntime.EnumerateModules()
                        .Where((module) => module.MetadataAddress != 0 && module.IsPEFile && !module.IsDynamic)
                        .Select((module) => new MetadataRegion(this, module))
                        .ToImmutableArray()
                        .Sort();
                }
            }

            if (_regions != null)
            {
                int min = 0, max = _regions.Length - 1;
                while (min <= max)
                {
                    int mid = (min + max) / 2;
                    MetadataRegion region = _regions[mid];

                    if (address >= region.StartAddress && address < region.EndAddress)
                    {
                        return region;
                    }

                    if (region.StartAddress < address)
                    {
                        min = mid + 1;
                    }
                    else
                    {
                        max = mid - 1;
                    }
                }
            }

            return null;
        }

        private ImmutableArray<byte> GetMetaDataFromAssembly(ClrModule module)
        {
            Debug.Assert(module.ImageBase != 0);

            var metadata = ImmutableArray<byte>.Empty;
            bool isVirtual = module.Layout != ModuleLayout.Flat;
            try
            {
                ulong size = module.Size;
                if (size == 0) {
                    size = 4096;
                }
                Stream stream = _memoryService.CreateMemoryStream(module.ImageBase, size);
                var peImage = new PEImage(stream, leaveOpen: false, isVirtual);
                if (peImage.IsValid)
                {
                    metadata = _symbolService.GetMetadata(module.Name, (uint)peImage.IndexTimeStamp, (uint)peImage.IndexFileSize);
                }
                else
                {
                    Trace.TraceError($"GetMetaData: {module.ImageBase:X16} not valid PE");
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException || ex is EndOfStreamException || ex is IOException)
            {
                Trace.TraceError($"GetMetaData: loaded {module.ImageBase:X16} exception {ex.Message}");
            }
            return metadata;
        }

        class MetadataRegion : IComparable<MetadataRegion>
        {
            private readonly MetadataMappingMemoryService _memoryService;
            private readonly ClrModule _module;

            internal ulong StartAddress => _module.MetadataAddress;

            internal ulong EndAddress => _module.MetadataAddress + _module.MetadataLength;

            private ImmutableArray<byte> _metadata;

            internal MetadataRegion(MetadataMappingMemoryService memoryService, ClrModule module)
            {
                _memoryService = memoryService;
                _module = module;
            }

            public int CompareTo(MetadataRegion region)
            {
                return StartAddress.CompareTo(region.StartAddress);
            }

            internal bool ReadMetaData(ulong address, Span<byte> buffer, out int bytesRead)
            {
                ImmutableArray<byte> metadata = GetMetaData();
                if (!metadata.IsDefaultOrEmpty)
                {
                    bytesRead = Math.Min(buffer.Length, metadata.Length);
                    int offset = (int)(address - StartAddress);
                    metadata.AsSpan().Slice(offset, bytesRead).CopyTo(buffer);
                    return true;
                }
                bytesRead = 0;
                return false;
            }

            private ImmutableArray<byte> GetMetaData()
            {
                if (_metadata.IsDefault)
                {
                    _metadata = _memoryService.GetMetaDataFromAssembly(_module);
                }
                return _metadata;
            }

            public override string ToString()
            {
                return $"{StartAddress} {EndAddress} {_module.Name}";
            }
        }
    }
}
