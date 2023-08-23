// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.Runtime.DacInterface;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed unsafe class ClrRuntimeHelpers : IClrRuntimeHelpers, IClrAppDomainHelpers
    {
        private ClrRuntime? _runtime;

        private readonly IDataReader _dataReader;
        private readonly ThreadStoreData _threadStore;
        private readonly ClrInfo _clrInfo;
        private readonly DacLibrary _library;
        private readonly ClrDataProcess _dac;
        private readonly SOSDac _sos;
        private readonly SOSDac6? _sos6;
        private readonly SOSDac8? _sos8;
        private readonly SosDac12? _sos12;
        private readonly ISOSDac13? _sos13;
        private readonly CacheOptions _cacheOptions;
        private readonly IClrModuleHelpers _moduleHelpers;
        private ClrAppDomainData? _domainData;
        private IClrNativeHeapHelpers? _nativeHeapHelpers;

        public ClrRuntimeHelpers(ClrInfo clrInfo, DacLibrary library, CacheOptions cacheOptions)
        {
            _clrInfo = clrInfo;
            _dataReader = clrInfo.DataTarget.DataReader;
            _library = library;
            _dac = library.DacPrivateInterface;
            _sos = library.SOSDacInterface;
            _sos6 = library.SOSDacInterface6;
            _sos8 = library.SOSDacInterface8;
            _sos12 = library.SOSDacInterface12;
            _sos13 = library.SOSDacInterface13;
            _cacheOptions = cacheOptions;
            _moduleHelpers = new ClrModuleHelpers(_sos, _dataReader, this);

            int version = 0;
            if (!_dac.Request(DacRequests.VERSION, ReadOnlySpan<byte>.Empty, new Span<byte>(&version, sizeof(int))))
                throw new InvalidDataException("This instance of CLR either has not been initialized or does not contain any data.  Failed to request DacVersion.");

            if (version != 9)
                throw new NotSupportedException($"The CLR debugging layer reported a version of {version} which this build of ClrMD does not support.");

            if (!_sos.GetThreadStoreData(out _threadStore))
                throw new InvalidDataException("This instance of CLR either has not been initialized or does not contain any data.    Failed to request ThreadStoreData.");

            library.DacDataTarget.SetMagicCallback(_dac.Flush);
        }

        public void Dispose()
        {
            Flush();
            _dac.Dispose();
            _sos.Dispose();
            _sos6?.Dispose();
            _sos8?.Dispose();
            _sos12?.Dispose();
            _sos13?.Dispose();
            _library.Dispose();
        }

        public ClrHeap CreateHeap()
        {
            ClrHeapHelpers helpers = new(_dac, _sos, _sos6, _sos8, _sos12, _dataReader, _cacheOptions);
            return new ClrHeap(Runtime, _dataReader, helpers);
        }

        public ClrRuntime Runtime
        {
            get
            {
                if (_runtime is null)
                    throw new InvalidOperationException($"Must set {nameof(ClrRuntimeHelpers)}.{nameof(Runtime)} before using it!");

                return _runtime;
            }
            set
            {
                if (_runtime is not null && _runtime != value)
                    throw new InvalidOperationException($"Cannot change {nameof(ClrRuntimeHelpers)}.{nameof(Runtime)}!");

                _runtime = value;
            }
        }

        public void Flush()
        {
            _domainData = null;
            _nativeHeapHelpers = null;
            FlushDac();
        }

        private void FlushDac()
        {
            if (_sos13 is not null && _sos13.LockedFlush())
                return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // IXClrDataProcess::Flush is unfortunately not wrapped with DAC_ENTER.  This means that
                // when it starts deleting memory, it's completely unsynchronized with parallel reads
                // and writes, leading to heap corruption and other issues.  This means that in order to
                // properly clear dac data structures, we need to trick the dac into entering the critical
                // section for us so we can call Flush safely then.

                // To accomplish this, we set a hook in our implementation of IDacDataTarget::ReadVirtual
                // which will call IXClrDataProcess::Flush if the dac tries to read the address set by
                // MagicCallbackConstant.  Additionally we make sure this doesn't interfere with other
                // reads by 1) Ensuring that the address is in kernel space, 2) only calling when we've
                // entered a special context.

                _library.DacDataTarget.EnterMagicCallbackContext();
                try
                {
                    _sos.GetWorkRequestData(DacDataTarget.MagicCallbackConstant, out _);
                }
                finally
                {
                    _library.DacDataTarget.ExitMagicCallbackContext();
                }
            }
            else
            {
                // On Linux/MacOS, skip the above workaround because calling Flush() in the DAC data target's
                // ReadVirtual function can cause a SEGSIGV because of an access of freed memory causing the
                // tool/app running CLRMD to crash. On Windows, it would be caught by the SEH try/catch handler
                // in DAC enter/leave code.

                _dac.Flush();
            }
        }

        public IClrNativeHeapHelpers GetNativeHeapHelpers()
        {
            IClrNativeHeapHelpers? helpers = _nativeHeapHelpers;
            if (helpers is null)
            {
                // We don't care if this races
                helpers = new ClrNativeHeapHelpers(_clrInfo, _sos, _sos13, _dataReader);
                _nativeHeapHelpers = helpers;
            }

            return helpers;
        }

        public ClrAppDomainData GetAppDomainData()
        {
            if (_domainData is null)
            {
                _ = _sos.GetAppDomainStoreData(out AppDomainStoreData domainStore);

                ClrAppDomainData domainData = new();
                domainData.SystemDomain = CreateAppDomain(domainStore.SystemDomain, "System Domain", domainData.Modules);

                if (domainStore.SharedDomain != 0)
                    domainData.SharedDomain = CreateAppDomain(domainStore.SharedDomain, "Shared Domain", domainData.Modules);

                ImmutableArray<ClrAppDomain>.Builder builder = ImmutableArray.CreateBuilder<ClrAppDomain>(domainStore.AppDomainCount);
                ClrDataAddress[] domainList = _sos.GetAppDomainList(domainStore.AppDomainCount);

                for (int i = 0; i < domainList.Length; i++)
                {
                    ClrAppDomain? domain = CreateAppDomain(domainList[i], null, domainData.Modules);
                    if (domain is not null)
                        builder.Add(domain);
                }

                domainData.AppDomains = builder.MoveOrCopyToImmutable();

                ClrModule? bcl = null;
                if (_sos.GetCommonMethodTables(out CommonMethodTables mts))
                    if (_sos.GetMethodTableData(mts.ObjectMethodTable, out MethodTableData mtData))
                        domainData.Modules.TryGetValue(mtData.Module, out bcl);

                if (bcl is null)
                {
                    string bclName = _clrInfo.Flavor == ClrFlavor.Core
                        ? "SYSTEM.PRIVATE.CORELIB"
                        : "MSCORLIB";

                    foreach (ClrModule module in domainData.Modules.Values)
                    {
                        if (module.Name == bclName)
                        {
                            bcl = module;
                            break;
                        }
                    }

                    bcl ??= new(domainData.SystemDomain!, _moduleHelpers, 0);
                }



                domainData.BaseClassLibrary = bcl;
                Interlocked.CompareExchange(ref _domainData, domainData, null);
            }

            return _domainData;
        }

        private ClrAppDomain? CreateAppDomain(ulong domainAddress, string? name, Dictionary<ulong, ClrModule> modules)
        {
            int id = -1;
            if (_sos.GetAppDomainData(domainAddress, out AppDomainData data))
                id = data.Id;

            name ??= _sos.GetAppDomainName(domainAddress);
            ClrAppDomain result = new(Runtime, this, domainAddress, name, id);

            ImmutableArray<ClrModule>.Builder moduleBuilder = ImmutableArray.CreateBuilder<ClrModule>();
            foreach (ulong assembly in _sos.GetAssemblyList(domainAddress))
                foreach (ulong moduleAddress in _sos.GetModuleList(assembly))
                {
                    if (modules.TryGetValue(moduleAddress, out ClrModule? module))
                    {
                        moduleBuilder.Add(module);
                    }
                    else
                    {
                        if (_sos.GetModuleData(moduleAddress, out ModuleData moduleData))
                            module = new(result, moduleAddress, _moduleHelpers, moduleData);
                        else
                            module = new(result, _moduleHelpers, moduleAddress);

                        modules.Add(moduleAddress, module);
                        moduleBuilder.Add(module);
                    }
                }

            result.Modules = moduleBuilder.MoveOrCopyToImmutable();
            return result;
        }

        public ClrMethod? GetMethodByMethodDesc(ulong methodDesc)
        {
            if (!_sos.GetMethodDescData(methodDesc, 0, out MethodDescData mdData))
                return null;

            ClrType? type = Runtime.Heap.GetTypeByMethodTable(mdData.MethodTable);
            if (type is null)
                return null;

            return type.Methods.FirstOrDefault(m => m.MethodDesc == methodDesc);
        }

        public ClrMethod? GetMethodByInstructionPointer(ulong ip)
        {
            ulong md = _sos.GetMethodDescPtrFromIP(ip);
            if (md == 0)
            {
                if (!_sos.GetCodeHeaderData(ip, out CodeHeaderData codeHeaderData))
                    return null;

                if ((md = codeHeaderData.MethodDesc) == 0)
                    return null;
            }

            return GetMethodByMethodDesc(md);
        }

        public IEnumerable<ClrThread> EnumerateThreads()
        {
            ClrAppDomainData domainData = GetAppDomainData();

            ClrThreadHelpers helpers = new(_dac, _sos, _dataReader);

            HashSet<ulong> seen = new() { 0 };
            ulong addr = _threadStore.FirstThread;

            int i;
            for (i = 0; i < _threadStore.ThreadCount && seen.Add(addr); i++)
            {
                if (!_sos.GetThreadData(addr, out ThreadData threadData))
                    break;

                yield return new(helpers, Runtime, domainData.GetDomainByAddress(threadData.Domain), addr, threadData, addr == _threadStore.FinalizerThread, addr == _threadStore.GCThread);

                addr = threadData.NextThread;
            }
        }

        public string? GetApplicationBase(ClrAppDomain domain) => _sos.GetAppBase(domain.Address);

        public string? GetConfigFile(ClrAppDomain domain) => _sos.GetAppBase(domain.Address);

        public ulong GetLoaderAllocator(ClrAppDomain domain)
        {
            if (_sos13 is null)
                return 0;

            return _sos13.GetDomainLoaderAllocator(domain.Address);
        }

        public IEnumerable<ClrJitManager> EnumerateClrJitManagers()
        {
            foreach (JitManagerInfo jitMgr in _sos.GetJitManagers())
                yield return new ClrJitManager(Runtime, jitMgr, GetNativeHeapHelpers());
        }

        public IEnumerable<ClrHandle> EnumerateHandles()
        {
            ClrAppDomainData appDomainData = GetAppDomainData();

            using SOSHandleEnum? handleEnum = _sos.EnumerateHandles();
            if (handleEnum is null)
                yield break;

            ClrHeap heap = Runtime.Heap;
            foreach (HandleData handle in handleEnum.ReadHandles())
            {
                ulong objAddress = _dataReader.ReadPointer(handle.Handle);
                ClrObject clrObj = heap.GetObject(objAddress);

                if (!clrObj.IsNull)
                {
                    ClrAppDomain? domain = appDomainData.GetDomainByAddress(handle.AppDomain);
                    domain ??= appDomainData.SystemDomain ?? appDomainData.SharedDomain ?? appDomainData.AppDomains.First();

                    ClrHandleKind handleKind = (ClrHandleKind)handle.Type;
                    switch (handleKind)
                    {
                        default:
                            yield return new ClrHandle(domain, handle.Handle, clrObj, handleKind);
                            break;

                        case ClrHandleKind.Dependent:
                            ClrObject dependent = heap.GetObject(handle.Secondary);
                            yield return new ClrHandle(domain, handle.Handle, clrObj, handleKind, dependent);
                            break;

                        case ClrHandleKind.RefCounted:
                            uint refCount = 0;

                            if (handle.IsPegged != 0)
                                refCount = handle.JupiterRefCount;

                            if (refCount < handle.RefCount)
                                refCount = handle.RefCount;

                            if (!clrObj.IsNull)
                            {
                                ComCallableWrapper? ccw = clrObj.GetComCallableWrapper();
                                if (ccw != null && refCount < ccw.RefCount)
                                {
                                    refCount = (uint)ccw.RefCount;
                                }
                                else
                                {
                                    RuntimeCallableWrapper? rcw = clrObj.GetRuntimeCallableWrapper();
                                    if (rcw != null && refCount < rcw.RefCount)
                                        refCount = (uint)rcw.RefCount;
                                }
                            }

                            yield return new ClrHandle(domain, handle.Handle, clrObj, handleKind, refCount);
                            break;
                    }
                }
            }
        }

        public IEnumerable<ClrNativeHeapInfo> EnumerateGCFreeRegions()
        {
            using (SosMemoryEnum? memoryEnum = _sos13?.GetGCFreeRegions())
            {
                if (memoryEnum is not null)
                    foreach (SosMemoryRegion mem in memoryEnum)
                    {
                        NativeHeapKind kind = (ulong)mem.ExtraData switch
                        {
                            1 => NativeHeapKind.GCFreeGlobalHugeRegion,
                            2 => NativeHeapKind.GCFreeGlobalRegion,
                            3 => NativeHeapKind.GCFreeRegion,
                            4 => NativeHeapKind.GCFreeSohSegment,
                            5 => NativeHeapKind.GCFreeUohSegment,
                            _ => NativeHeapKind.GCFreeRegion
                        };

                        ulong raw = (ulong)mem.Start;
                        ulong start = raw & ~0xfful;
                        ulong diff = raw - start;
                        ulong len = mem.Length + diff;

                        yield return new ClrNativeHeapInfo(MemoryRange.CreateFromLength(start, len), kind, ClrNativeHeapState.Inactive, mem.Heap);
                    }
            }
        }

        public IEnumerable<ClrNativeHeapInfo> EnumerateHandleTableRegions()
        {
            using (SosMemoryEnum? memoryEnum = _sos13?.GetHandleTableRegions())
            {
                if (memoryEnum is not null)
                    foreach (SosMemoryRegion mem in memoryEnum)
                        yield return new ClrNativeHeapInfo(MemoryRange.CreateFromLength(mem.Start, mem.Length), NativeHeapKind.HandleTable, ClrNativeHeapState.Active, mem.Heap);
            }
        }

        public IEnumerable<ClrNativeHeapInfo> EnumerateGCBookkeepingRegions()
        {
            using (SosMemoryEnum? memoryEnum = _sos13?.GetGCBookkeepingMemoryRegions())
            {
                if (memoryEnum is not null)
                    foreach (SosMemoryRegion mem in memoryEnum)
                        yield return new ClrNativeHeapInfo(MemoryRange.CreateFromLength(mem.Start, mem.Length), NativeHeapKind.GCBookkeeping, ClrNativeHeapState.RegionOfRegions);
            }
        }

        public string? GetJitHelperFunctionName(ulong address) => _sos.GetJitHelperFunctionName(address);

        public ClrThreadPool? GetThreadPool()
        {
            ClrThreadPoolHelper helper = new(_sos);
            ClrThreadPool result = new(Runtime, helper);
            return result.Initialized ? result : null;
        }

        public IEnumerable<ClrSyncBlockCleanupData> EnumerateSyncBlockCleanupData()
        {
            ulong loopCheck = 0;
            while (_sos.GetSyncBlockCleanupData(0, out SyncBlockCleanupData data))
            {
                if (loopCheck == 0)
                    loopCheck = data.NextSyncBlock;
                else if (loopCheck == data.NextSyncBlock)
                    break;

                yield return new(data.SyncBlockPointer, data.BlockRCW, data.BlockCCW, data.BlockClassFactory);
            }
        }

        public IEnumerable<ClrRcwCleanupData> EnumerateRcwCleanupData()
        {
            return _sos.EnumerateRCWCleanup(0).Select(r => new ClrRcwCleanupData(r.Rcw, r.Context, r.Thread, r.IsFreeThreaded));
        }
    }
}
