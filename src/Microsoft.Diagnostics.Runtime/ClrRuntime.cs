// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime

{
    /// <summary>
    /// Represents a single runtime in a target process or crash dump.  This serves as the primary
    /// entry point for getting diagnostic information.
    /// </summary>
    public sealed class ClrRuntime : IClrRuntime
    {
        private readonly IClrRuntimeHelpers _helpers;
        private volatile ClrAppDomainData? _appDomainData;
        private volatile ClrHeap? _heap;
        private ImmutableArray<ClrThread> _threads;

        internal ClrRuntime(ClrInfo clrInfo, DacLibrary library)
        {
            ClrInfo = clrInfo;
            DataTarget = clrInfo.DataTarget;
            DacLibrary = library;
            _helpers = new ClrRuntimeHelpers(clrInfo, DacLibrary, DataTarget.CacheOptions)
            {
                Runtime = this
            };
        }

        internal ClrRuntime(ClrInfo clrInfo, DacLibrary library, IClrRuntimeHelpers helpers)
        {
            ClrInfo = clrInfo;
            DataTarget = clrInfo.DataTarget;
            DacLibrary = library;
            _helpers = helpers;
        }

        private ClrAppDomainData GetAppDomainData()
        {
            if (_appDomainData is not null)
                return _appDomainData;

            ClrAppDomainData data = _helpers.GetAppDomainData();
            Interlocked.CompareExchange(ref _appDomainData, data, null);
            return _appDomainData;
        }

        /// <summary>
        /// Used for internal purposes.
        /// </summary>
        public DacLibrary DacLibrary { get; }

        /// <summary>
        /// Gets the <see cref="ClrInfo"/> of the current runtime.
        /// </summary>
        public ClrInfo ClrInfo { get; }

        /// <summary>
        /// Gets the <see cref="DataTarget"/> associated with this runtime.
        /// </summary>
        public DataTarget DataTarget { get; }

        /// <summary>
        /// Returns whether you are allowed to call into the transitive closure of ClrMD objects created from
        /// this runtime on multiple threads.
        /// </summary>
        public bool IsThreadSafe => DataTarget.DataReader.IsThreadSafe;

        /// <summary>
        /// Gets the list of appdomains in the process.
        /// </summary>
        public ImmutableArray<ClrAppDomain> AppDomains => GetAppDomainData().AppDomains;

        /// <summary>
        /// Gets the System AppDomain for Desktop CLR (<see langword="null"/> on .NET Core).
        /// </summary>
        public ClrAppDomain? SystemDomain => GetAppDomainData().SystemDomain;

        /// <summary>
        /// Gets the Shared AppDomain for Desktop CLR (<see langword="null"/> on .NET Core).
        /// </summary>
        public ClrAppDomain? SharedDomain => GetAppDomainData().SharedDomain;

        public ClrModule BaseClassLibrary => GetAppDomainData().BaseClassLibrary!;

        /// <summary>
        /// Gets information about CLR's ThreadPool.  May return null if we could not obtain
        /// ThreadPool data from the target process or dump.
        /// </summary>
        public ClrThreadPool? ThreadPool => _helpers.GetThreadPool();

        /// <summary>
        /// Gets all managed threads in the process.  Only threads which have previously run managed
        /// code will be enumerated.
        /// </summary>
        public ImmutableArray<ClrThread> Threads
        {
            get
            {
                if (!_threads.IsDefault)
                    return _threads;

                ImmutableArray<ClrThread> threads = _helpers.EnumerateThreads().ToImmutableArray();
                ImmutableInterlocked.InterlockedCompareExchange(ref _threads, threads, _threads);

                return _threads;
            }
        }

        /// <summary>
        /// Returns a ClrMethod by its internal runtime handle (on desktop CLR this is a MethodDesc).
        /// </summary>
        /// <param name="methodHandle">The method handle (MethodDesc) to look up.</param>
        /// <returns>The ClrMethod for the given method handle, or <see langword="null"/> if no method was found.</returns>
        public ClrMethod? GetMethodByHandle(ulong methodHandle) => _helpers.GetMethodByMethodDesc(methodHandle);

        /// <summary>
        /// Gets the <see cref="ClrType"/> corresponding to the given MethodTable.
        /// </summary>
        /// <param name="methodTable">The ClrType.MethodTable for the requested type.</param>
        /// <returns>A ClrType object, or <see langword="null"/> if no such type exists.</returns>
        public ClrType? GetTypeByMethodTable(ulong methodTable) => Heap.GetTypeByMethodTable(methodTable);

        /// <summary>
        /// Enumerates a list of GC handles currently in the process.  Note that this list may be incomplete
        /// depending on the state of the process when we attempt to walk the handle table.
        /// </summary>
        /// <returns>The list of GC handles in the process, NULL on catastrophic error.</returns>
        public IEnumerable<ClrHandle> EnumerateHandles() => _helpers.EnumerateHandles();

        /// <summary>
        /// Gets the GC heap of the process.
        /// </summary>
        public ClrHeap Heap
        {
            get
            {
                ClrHeap? heap = _heap;
                if (heap is null)
                {
                    heap = _helpers.CreateHeap();
                    Interlocked.CompareExchange(ref _heap, heap, null);
                    heap = _heap;
                }

                return heap;
            }
        }

        IClrThreadPool? IClrRuntime.ThreadPool => ThreadPool;

        IClrHeap IClrRuntime.Heap => Heap;

        ImmutableArray<IClrAppDomain> IClrRuntime.AppDomains => AppDomains.CastArray<IClrAppDomain>();

        IClrAppDomain? IClrRuntime.SharedDomain => SharedDomain;

        IClrAppDomain? IClrRuntime.SystemDomain => SystemDomain;

        ImmutableArray<IClrThread> IClrRuntime.Threads => Threads.CastArray<IClrThread>();

        IClrInfo IClrRuntime.ClrInfo => ClrInfo;

        IDataTarget IClrRuntime.DataTarget => DataTarget;

        IClrModule IClrRuntime.BaseClassLibrary => BaseClassLibrary;

        /// <summary>
        /// Attempts to get a ClrMethod for the given instruction pointer.  This will return NULL if the
        /// given instruction pointer is not within any managed method.
        /// </summary>
        public ClrMethod? GetMethodByInstructionPointer(ulong ip) => _helpers.GetMethodByInstructionPointer(ip);

        /// <summary>
        /// Enumerate all managed modules in the runtime.
        /// </summary>
        public IEnumerable<ClrModule> EnumerateModules() => GetAppDomainData().Modules.Values;

        /// <summary>
        /// Enumerates all native heaps that CLR has allocated.  This method is used to give insights into
        /// what native memory ranges are owned by CLR.  For example, this is the information enumerated
        /// by SOS's !eeheap and "!ext maddress".
        /// </summary>
        /// <returns>An enumeration of heaps.</returns>
        public IEnumerable<ClrNativeHeapInfo> EnumerateClrNativeHeaps()
        {
            // Enumerate the JIT code heaps.
            foreach (ClrJitManager jitMgr in EnumerateJitManagers())
                foreach (ClrNativeHeapInfo heap in jitMgr.EnumerateNativeHeaps())
                    yield return heap;

            HashSet<ulong> visited = new();

            // Ensure we are working on a consistent set of domains/modules
            ClrAppDomainData domainData = GetAppDomainData();

            // Walk domains
            if (domainData.SystemDomain is not null)
            {
                visited.Add(domainData.SystemDomain.LoaderAllocator);
                foreach (ClrNativeHeapInfo heap in domainData.SystemDomain.EnumerateLoaderAllocatorHeaps())
                    yield return heap;
            }

            if (domainData.SharedDomain is not null)
            {
                visited.Add(domainData.SharedDomain.LoaderAllocator);
                foreach (ClrNativeHeapInfo heap in domainData.SharedDomain.EnumerateLoaderAllocatorHeaps())
                    yield return heap;
            }

            foreach (ClrAppDomain domain in domainData.AppDomains)
            {
                if (domain.LoaderAllocator == 0 || visited.Add(domain.LoaderAllocator))
                    foreach (ClrNativeHeapInfo heap in domain.EnumerateLoaderAllocatorHeaps())
                        yield return heap;
            }

            // Walk modules.  We do this after domains to ensure we don't enumerate
            // previously enumerated LoaderAllocators.
            foreach (ClrModule module in domainData.Modules.Values)
            {
                // We don't want to skip modules with no address, as we might have
                // multiple of those with unique heaps.
                if (module.Address == 0 || visited.Add(module.Address))
                {
                    if (module.ThunkHeap != 0 && visited.Add(module.ThunkHeap))
                        foreach (ClrNativeHeapInfo heap in module.EnumerateThunkHeap())
                            yield return heap;

                    // LoaderAllocator may be shared with its parent domain.  We only have a
                    // unique LoaderAllocator in the case of collectable assemblies.
                    if (module.LoaderAllocator != 0 && visited.Add(module.LoaderAllocator))
                        foreach (ClrNativeHeapInfo heap in module.EnumerateLoaderAllocatorHeaps())
                            yield return heap;
                }
            }

            foreach (ClrNativeHeapInfo gcFreeRegion in _helpers.EnumerateGCFreeRegions())
            {
                yield return gcFreeRegion;
            }

            foreach (ClrNativeHeapInfo handleHeap in _helpers.EnumerateHandleTableRegions())
            {
                yield return handleHeap;
            }

            foreach (ClrNativeHeapInfo bkRegions in _helpers.EnumerateGCBookkeepingRegions())
            {
                yield return bkRegions;
            }
        }

        public IEnumerable<ClrSyncBlockCleanupData> EnumerateSyncBlockCleanupData() => _helpers.EnumerateSyncBlockCleanupData();
        public IEnumerable<ClrRcwCleanupData> EnumerateRcwCleanupData() => _helpers.EnumerateRcwCleanupData();

        /// <summary>
        /// Enumerates native heaps that the JIT has allocated.
        /// </summary>
        /// <returns>An enumeration of heaps.</returns>
        public IEnumerable<ClrJitManager> EnumerateJitManagers() => _helpers.EnumerateClrJitManagers();

        /// <summary>
        /// Flushes the DAC cache.  This function MUST be called any time you expect to call the same function
        /// but expect different results.  For example, after walking the heap, you need to call Flush before
        /// attempting to walk the heap again.  After calling this function, you must discard ALL ClrMD objects
        /// you have cached other than DataTarget and ClrRuntime and re-request the objects and data you need.
        /// (e.g. if you want to use the ClrHeap object after calling flush, you must call ClrRuntime.GetHeap
        /// again after Flush to get a new instance.)
        /// </summary>
        public void FlushCachedData()
        {
            _appDomainData = null;
            _threads = default;
            _heap = null;
            _helpers.Flush();
        }

        /// <summary>
        /// Gets the name of a JIT helper function.
        /// </summary>
        /// <param name="address">Address of a possible JIT helper function.</param>
        /// <returns>The name of the JIT helper function or <see langword="null"/> if <paramref name="address"/> isn't a JIT helper function.</returns>
        public string? GetJitHelperFunctionName(ulong address) => _helpers.GetJitHelperFunctionName(address);

        /// <summary>
        /// Cleans up all resources and releases them.  You may not use this ClrRuntime or any object it transitively
        /// created after calling this method.
        /// </summary>
        public void Dispose()
        {
            FlushCachedData();
            _helpers.Dispose();
        }

        IEnumerable<IClrRoot> IClrRuntime.EnumerateHandles() => EnumerateHandles().Cast<IClrRoot>();

        IEnumerable<IClrJitManager> IClrRuntime.EnumerateJitManagers() => EnumerateJitManagers().Cast<IClrJitManager>();

        IEnumerable<IClrModule> IClrRuntime.EnumerateModules() => EnumerateModules().Cast<IClrModule>();

        IClrMethod? IClrRuntime.GetMethodByHandle(ulong methodHandle) => GetMethodByHandle(methodHandle);

        IClrMethod? IClrRuntime.GetMethodByInstructionPointer(ulong ip) => GetMethodByInstructionPointer(ip);

        IClrType? IClrRuntime.GetTypeByMethodTable(ulong methodTable) => GetTypeByMethodTable(methodTable);
    }
}