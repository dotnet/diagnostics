// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Runtime;
using Microsoft.FileFormats;
using Microsoft.FileFormats.PE;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Memory service wrapper that maps always module's metadata into the address
    /// space even is some or all of the memory exists in the coredump. lldb returns
    /// zero's (instead of failing the memory read) for missing pages in core dumps
    /// that older (less than 5.0) createdumps generate  so it needs this special
    /// metadata  mapping memory service.
    /// </summary>
    public class MetadataMappingMemoryService : IMemoryService, IDisposable
    {
        private readonly ServiceContainer _serviceContainer;
        private readonly IMemoryService _memoryService;
        private readonly IRuntimeService _runtimeService;
        private readonly ISymbolService _symbolService;
        private readonly IDisposable _onChangeEvent;
        private bool _regionInitialized;
        private ImmutableArray<MetadataRegion> _regions;

        /// <summary>
        /// Memory service constructor
        /// </summary>
        /// <param name="container">service container</param>
        /// <param name="memoryService">memory service to wrap</param>
        public MetadataMappingMemoryService(ServiceContainer container, IMemoryService memoryService)
        {
            _serviceContainer = container;
            container.AddService(memoryService);

            _memoryService = memoryService;
            _runtimeService = container.GetService<IRuntimeService>();
            _symbolService = container.GetService<ISymbolService>();

            ITarget target = container.GetService<ITarget>();
            target.OnFlushEvent.Register(Flush);

            ISymbolService symbolService = container.GetService<ISymbolService>();
            _onChangeEvent = symbolService?.OnChangeEvent.Register(Flush);
        }

        public void Dispose()
        {
            Flush();
            _onChangeEvent?.Dispose();
            _serviceContainer.RemoveService(typeof(IMemoryService));
            _serviceContainer.DisposeServices();
            if (_memoryService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// Flush the metadata memory service
        /// </summary>
        private void Flush()
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
                // Need to set this before enumerating the runtimes to prevent reentrancy
                _regionInitialized = true;

                var runtimes = _runtimeService.EnumerateRuntimes();
                if (runtimes.Any())
                {
                    foreach (IRuntime runtime in runtimes)
                    {
                        Trace.TraceInformation($"FindRegion: initializing regions for runtime #{runtime.Id}");
                        ClrRuntime clrRuntime = runtime.Services.GetService<ClrRuntime>();
                        if (clrRuntime is not null)
                        {
                            Trace.TraceInformation($"FindRegion: initializing regions for ClrRuntime #{runtime.Id}");
                            _regions = clrRuntime.EnumerateModules()
                                .Where((module) => module.MetadataAddress != 0 && module.IsPEFile && !module.IsDynamic)
                                .Select((module) => new MetadataRegion(this, module))
                                .ToImmutableArray()
                                .Sort();
                        }
                    }
                }
                else
                {
                    // If there are no runtimes, try again next time around
                    _regionInitialized = false;
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
                Stream stream = _memoryService.CreateMemoryStream(module.ImageBase, module.Size > 0 ? module.Size : 4096);
                PEFile peFile = new(new StreamAddressSpace(stream), isVirtual);
                if (peFile.IsValid())
                {
                    metadata = _symbolService.GetMetadata(module.Name, peFile.Timestamp, peFile.SizeOfImage);
                }
                else
                {
                    Trace.TraceError($"GetMetaData: {module.ImageBase:X16} not valid PE");
                }
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException)
            {
                Trace.TraceError($"GetMetaData: loaded {module.ImageBase:X16} exception {ex.Message}");
            }
            return metadata;
        }

        private class MetadataRegion : IComparable<MetadataRegion>
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
