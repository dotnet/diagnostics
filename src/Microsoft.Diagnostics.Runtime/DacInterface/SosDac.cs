// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    /// <summary>
    /// This is an undocumented, untested, and unsupported interface.  Do not use.
    /// </summary>
    internal sealed unsafe class SOSDac : CallableCOMWrapper
    {
        internal static readonly Guid IID_ISOSDac = new("436f00f2-b42a-4b9f-870c-e73db66ae930");

        private readonly DacLibrary _library;

        public SOSDac(DacLibrary? library, IntPtr ptr)
            : base(library?.OwningLibrary, IID_ISOSDac, ptr)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
        }

        private ref readonly ISOSDacVTable VTable => ref Unsafe.AsRef<ISOSDacVTable>(_vtable);

        public SOSDac(DacLibrary lib, CallableCOMWrapper toClone) : base(toClone)
        {
            _library = lib;
        }

        public RejitData[] GetRejitData(ulong md, ulong ip = 0)
        {
            HResult hr = VTable.GetMethodDescData(Self, md, ip, out _, 0, null, out int needed);
            if (hr && needed >= 1)
            {
                RejitData[] result = new RejitData[needed];
                hr = VTable.GetMethodDescData(Self, md, ip, out _, result.Length, result, out _);
                if (hr)
                    return result;
            }

            return Array.Empty<RejitData>();
        }

        public HResult GetMethodDescData(ulong md, ulong ip, out MethodDescData data)
        {
            return VTable.GetMethodDescData(Self, md, ip, out data, 0, null, out _);
        }

        public HResult GetThreadStoreData(out ThreadStoreData data)
        {
            return VTable.GetThreadStoreData(Self, out data);
        }

        public string? GetRegisterName(int index)
        {
            // Register names shouldn't be big.
            Span<char> buffer = stackalloc char[32];

            fixed (char* ptr = buffer)
            {
                HResult hr = VTable.GetRegisterName(Self, index, buffer.Length, ptr, out int needed);
                if (!hr)
                    return null;

                if (needed == 0)
                    return string.Empty;

                int len = buffer.IndexOf((char)0);
                if (len >= 0)
                    buffer = buffer.Slice(0, len);

                return new string(ptr, 0, buffer.Length);
            }
        }

        public uint GetTlsIndex()
        {
            HResult hr = VTable.GetTLSIndex(Self, out uint index);
            if (hr)
                return index;

            return uint.MaxValue;
        }

        public ClrDataAddress GetThreadFromThinlockId(uint id)
        {
            HResult hr = VTable.GetThreadFromThinlockID(Self, id, out ClrDataAddress thread);
            if (hr)
                return thread;

            return default;
        }

        public string? GetMethodDescName(ulong md)
        {
            if (md == 0)
                return null;

            HResult hr = VTable.GetMethodDescName(Self, md, 0, null, out int needed);
            if (!hr)
                return null;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(needed * sizeof(char));
            try
            {
                int actuallyNeeded;
                fixed (byte* bufferPtr = buffer)
                {
                    hr = VTable.GetMethodDescName(Self, md, needed, bufferPtr, out actuallyNeeded);
                    if (!hr)
                        return null;
                }

                // Patch for a bug on sos side :
                //  Sometimes, when the target method has parameters with generic types
                //  the first call to GetMethodDescName sets an incorrect value into pNeeded.
                //  In those cases, a second call directly after the first returns the correct value.
                if (needed != actuallyNeeded)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = ArrayPool<byte>.Shared.Rent(actuallyNeeded * sizeof(char));
                    fixed (byte* bufferPtr = buffer)
                    {
                        hr = VTable.GetMethodDescName(Self, md, actuallyNeeded, bufferPtr, out actuallyNeeded);
                        if (!hr)
                            return null;
                    }
                }

                return Encoding.Unicode.GetString(buffer, 0, (actuallyNeeded - 1) * sizeof(char));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public ulong GetMethodTableSlot(ulong mt, uint slot)
        {
            if (mt == 0)
                return 0;

            HResult hr = VTable.GetMethodTableSlot(Self, mt, slot, out ClrDataAddress ip);
            if (hr)
                return ip;

            return 0;
        }

        public HResult GetThreadLocalModuleData(ulong thread, uint index, out ThreadLocalModuleData data)
        {
            return VTable.GetThreadLocalModuleData(Self, thread, index, out data);
        }

        public ulong GetILForModule(ulong moduleAddr, uint rva)
        {
            HResult hr = VTable.GetILForModule(Self, moduleAddr, rva, out ClrDataAddress result);
            if (hr)
                return result;

            return 0;
        }

        public COMInterfacePointerData[]? GetCCWInterfaces(ulong ccw, int count)
        {
            COMInterfacePointerData[] data = new COMInterfacePointerData[count];
            fixed (COMInterfacePointerData* ptr = data)
            {
                HResult hr = VTable.GetCCWInterfaces(Self, ccw, count, ptr, out int pNeeded);
                if (hr)
                    return data;
            }

            return null;
        }

        public COMInterfacePointerData[]? GetRCWInterfaces(ulong ccw, int count)
        {
            COMInterfacePointerData[] data = new COMInterfacePointerData[count];
            fixed (COMInterfacePointerData* ptr = data)
            {
                HResult hr = VTable.GetRCWInterfaces(Self, ccw, count, ptr, out int pNeeded);
                if (hr)
                    return data;
            }

            return null;
        }

        public HResult GetDomainLocalModuleDataFromModule(ulong module, out DomainLocalModuleData data)
        {
            return VTable.GetDomainLocalModuleDataFromModule(Self, module, out data);
        }

        public HResult GetDomainLocalModuleDataFromAppDomain(ulong appDomain, int id, out DomainLocalModuleData data)
        {
            return VTable.GetDomainLocalModuleDataFromAppDomain(Self, appDomain, id, out data);
        }

        public HResult GetWorkRequestData(ulong request, out WorkRequestData data)
        {
            return VTable.GetWorkRequestData(Self, request, out data);
        }

        public HResult GetThreadPoolData(out ThreadPoolData data)
        {
            return VTable.GetThreadpoolData(Self, out data);
        }

        public HResult GetSyncBlockData(int index, out SyncBlockData data)
        {
            return VTable.GetSyncBlockData(Self, index, out data);
        }

        public string? GetAppBase(ulong domain)
        {
            return GetString(VTable.GetApplicationBase, domain);
        }

        public string? GetConfigFile(ulong domain)
        {
            return GetString(VTable.GetAppDomainConfigFile, domain);
        }

        public HResult GetCodeHeaderData(ulong ip, out CodeHeaderData codeHeaderData)
        {
            if (ip == 0)
            {
                codeHeaderData = default;
                return HResult.E_INVALIDARG;
            }
            return VTable.GetCodeHeaderData(Self, ip, out codeHeaderData);
        }

        public ClrDataAddress GetMethodDescPtrFromFrame(ulong frame)
        {
            HResult hr = VTable.GetMethodDescPtrFromFrame(Self, frame, out ClrDataAddress data);
            if (hr)
                return data;

            return default;
        }

        public ClrDataAddress GetMethodDescPtrFromIP(ulong frame)
        {
            HResult hr = VTable.GetMethodDescPtrFromIP(Self, frame, out ClrDataAddress data);
            if (hr)
                return data;

            return default;
        }

        public string GetFrameName(ulong vtable)
        {
            return GetString(VTable.GetFrameName, vtable, false) ?? "Unknown Frame";
        }

        public HResult GetFieldInfo(ulong mt, out FieldInfo data)
        {
            return VTable.GetMethodTableFieldData(Self, mt, out data);
        }

        public HResult GetFieldData(ulong fieldDesc, out FieldData data)
        {
            return VTable.GetFieldDescData(Self, fieldDesc, out data);
        }

        public HResult GetObjectData(ulong obj, out ObjectData data)
        {
            return VTable.GetObjectData(Self, obj, out data);
        }

        public HResult GetCCWData(ulong ccw, out CcwData data)
        {
            return VTable.GetCCWData(Self, ccw, out data);
        }

        public HResult GetRCWData(ulong rcw, out RcwData data)
        {
            return VTable.GetRCWData(Self, rcw, out data);
        }

        public IEnumerable<(ulong Rcw, ulong Context, ulong Thread, bool IsFreeThreaded)> EnumerateRCWCleanup(ulong cleanupList)
        {
            List<(ulong, ulong, ulong, bool)> result = new();
            RcwCleanupTraverse traverse = (rcw, context, thread, freeThreaded, token) =>
            {
                result.Add((rcw, context, thread, freeThreaded != 0));

                return result.Count > 16384 ? 0 : 1u;
            };

            VTable.TraverseRCWCleanupList(Self, cleanupList, Marshal.GetFunctionPointerForDelegate(traverse), 0);

            GC.KeepAlive(traverse);
            return result;
        }

        public HResult GetSyncBlockCleanupData(ulong syncBlockCleanupPointer, out SyncBlockCleanupData data)
        {
            return VTable.GetSyncBlockCleanupData(Self, syncBlockCleanupPointer, out data);
        }

        public delegate uint RcwCleanupTraverse(ClrDataAddress rcw, ClrDataAddress context, ClrDataAddress thread, uint isFreeThreaded, IntPtr token);

        public ClrDataModule? GetClrDataModule(ulong module)
        {
            if (module == 0)
                return null;

            HResult hr = VTable.GetModule(Self, module, out IntPtr iunk);
            if (hr)
                return new ClrDataModule(_library, iunk);

            return null;
        }

        public MetadataImport? GetMetadataImport(ulong module)
        {
            if (module == 0)
                return null;

            HResult hr = VTable.GetModule(Self, module, out IntPtr iunk);
            if (!hr)
                return null;

            // Make sure we can successfully QueryInterface for IMetaDataImport.  This may fail if
            // we do not have all of the relevant metadata mapped into memory either through the dump
            // or via the binary locator.
            if (QueryInterface(iunk, MetadataImport.IID_IMetaDataImport, out IntPtr pTmp))
                Release(pTmp);
            else
                return null;

            try
            {
                return new MetadataImport(_library, iunk);
            }
            catch (InvalidCastException)
            {
                return null;
            }
        }

        public HResult GetCommonMethodTables(out CommonMethodTables commonMTs)
        {
            return VTable.GetUsefulGlobals(Self, out commonMTs);
        }

        public ClrDataAddress[] GetAssemblyList(ulong appDomain) => GetAssemblyList(appDomain, 0);

        public ClrDataAddress[] GetAssemblyList(ulong appDomain, int count) => GetModuleOrAssembly(appDomain, count, VTable.GetAssemblyList);

        public ClrDataAddress[] GetModuleList(ulong assembly) => GetModuleList(assembly, 0);

        public ClrDataAddress[] GetModuleList(ulong assembly, int count) => GetModuleOrAssembly(assembly, count, VTable.GetAssemblyModuleList);

        public HResult GetAssemblyData(ulong domain, ulong assembly, out AssemblyData data)
        {
            // The dac seems to have an issue where the assembly data can be filled in for a minidump.
            // If the data is partially filled in, we'll use it.

            HResult hr = VTable.GetAssemblyData(Self, domain, assembly, out data);
            if (!hr && data.Address == assembly)
                return HResult.S_FALSE;

            return hr;
        }

        public HResult GetAppDomainData(ulong addr, out AppDomainData data)
        {
            // We can face an exception while walking domain data if we catch the process
            // at a bad state.  As a workaround we will return partial data if data.Address
            // and data.StubHeap are set.

            HResult hr = VTable.GetAppDomainData(Self, addr, out data);
            if (!hr && data.Address == addr && data.StubHeap != 0)
                return HResult.S_FALSE;

            return hr;
        }

        public string? GetAppDomainName(ulong appDomain)
        {
            return GetString(VTable.GetAppDomainName, appDomain);
        }

        public string? GetAssemblyName(ulong assembly)
        {
            return GetString(VTable.GetAssemblyName, assembly);
        }

        public HResult GetAppDomainStoreData(out AppDomainStoreData data)
        {
            return VTable.GetAppDomainStoreData(Self, out data);
        }

        public HResult GetMethodTableData(ulong addr, out MethodTableData data)
        {
            // If the 2nd bit is set it means addr is actually a TypeHandle (which GetMethodTable does not support).
            if ((addr & 2) == 2)
            {
                data = default;
                return HResult.E_INVALIDARG;
            }
            return VTable.GetMethodTableData(Self, addr, out data);
        }

        public string? GetMethodTableName(ulong mt)
        {
            return GetString(VTable.GetMethodTableName, mt);
        }

        public string? GetJitHelperFunctionName(ulong addr)
        {
            return GetAsciiString(VTable.GetJitHelperFunctionName, addr);
        }

        public string? GetPEFileName(ulong pefile)
        {
            return GetString(VTable.GetPEFileName, pefile);
        }

        private string? GetString(delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> func, ulong addr, bool skipNull = true)
        {
            HResult hr = func(Self, addr, 0, null, out int needed);
            if (!hr)
                return null;

            if (needed == 0)
                return string.Empty;

            byte[]? array = null;
            int size = needed * sizeof(char);
            Span<byte> buffer = size <= 32 ? stackalloc byte[size] : (array = ArrayPool<byte>.Shared.Rent(size)).AsSpan(0, size);

            try
            {
                fixed (byte* bufferPtr = buffer)
                    hr = func(Self, addr, needed, bufferPtr, out needed);

                if (!hr)
                    return null;

                if (skipNull)
                    needed--;

                return Encoding.Unicode.GetString(buffer.Slice(0, needed * sizeof(char)));
            }
            finally
            {
                if (array != null)
                    ArrayPool<byte>.Shared.Return(array);
            }
        }

        private string? GetAsciiString(delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> func, ulong addr)
        {
            HResult hr = func(Self, addr, 0, null, out int needed);
            if (!hr)
                return null;

            if (needed == 0)
                return string.Empty;

            byte[]? array = null;
            Span<byte> buffer = needed <= 32 ? stackalloc byte[needed] : (array = ArrayPool<byte>.Shared.Rent(needed)).AsSpan(0, needed);

            try
            {
                fixed (byte* bufferPtr = buffer)
                    hr = func(Self, addr, needed, bufferPtr, out needed);

                if (!hr)
                    return null;

                int len = buffer.IndexOf((byte)'\0');
                if (len >= 0)
                    needed = len;

                return Encoding.ASCII.GetString(buffer.Slice(0, needed));
            }
            finally
            {
                if (array != null)
                    ArrayPool<byte>.Shared.Return(array);
            }
        }

        public ClrDataAddress GetMethodTableByEEClass(ulong eeclass)
        {
            HResult hr = VTable.GetMethodTableForEEClass(Self, eeclass, out ClrDataAddress data);
            if (hr)
                return data;

            return default;
        }

        public HResult GetModuleData(ulong module, out ModuleData data)
        {
            return VTable.GetModuleData(Self, module, out data);
        }

        private ClrDataAddress[] GetModuleOrAssembly(ulong address, int count, delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, ClrDataAddress*, out int, int> func)
        {
            int needed;
            if (count <= 0)
            {
                if (func(Self, address, 0, null, out needed) < 0)
                    return Array.Empty<ClrDataAddress>();

                count = needed;
            }

            // We ignore the return value here since the list may be partially filled
            ClrDataAddress[] modules = new ClrDataAddress[count];
            fixed (ClrDataAddress* ptr = modules)
                func(Self, address, modules.Length, ptr, out needed);

            return modules;
        }

        public ClrDataAddress[] GetAppDomainList(int count = 0)
        {
            if (count <= 0)
            {
                if (!GetAppDomainStoreData(out AppDomainStoreData addata))
                    return Array.Empty<ClrDataAddress>();

                count = addata.AppDomainCount;
            }

            ClrDataAddress[] data = new ClrDataAddress[count];
            fixed (ClrDataAddress* ptr = data)
            {
                HResult hr = VTable.GetAppDomainList(Self, data.Length, ptr, out int needed);
                return hr ? data : Array.Empty<ClrDataAddress>();
            }
        }

        public HResult GetThreadData(ulong address, out ThreadData data)
        {
            if (address == 0)
            {
                data = default;
                return HResult.E_INVALIDARG;
            }
            return VTable.GetThreadData(Self, address, out data);
        }

        public HResult GetGCHeapData(out GCInfo data)
        {
            return VTable.GetGCHeapData(Self, out data);
        }

        public HResult GetOOMData(out DacOOMData oomData) => VTable.GetOOMStaticData(Self, out oomData);

        public HResult GetOOMData(ulong address, out DacOOMData oomData) => VTable.GetOOMData(Self, address, out oomData);

        public HResult GetHeapAnalyzeData(out DacHeapAnalyzeData analyzeData) => VTable.GetHeapAnalyzeStaticData(Self, out analyzeData);

        public HResult GetHeapAnalyzeData(ulong address, out DacHeapAnalyzeData analyzeData) => VTable.GetHeapAnalyzeData(Self, address, out analyzeData);

        public HResult GetSegmentData(ulong addr, out SegmentData data)
        {
            return VTable.GetHeapSegmentData(Self, addr, out data);
        }

        public ClrDataAddress[] GetHeapList(int heapCount)
        {
            ClrDataAddress[] refs = new ClrDataAddress[heapCount];
            fixed (ClrDataAddress* ptr = refs)
            {
                HResult hr = VTable.GetGCHeapList(Self, heapCount, ptr, out int needed);
                return hr ? refs : Array.Empty<ClrDataAddress>();
            }
        }

        public HResult GetServerHeapDetails(ulong addr, out HeapDetails data)
        {
            return VTable.GetGCHeapDetails(Self, addr, out data);
        }

        public HResult GetWksHeapDetails(out HeapDetails data)
        {
            return VTable.GetGCHeapStaticData(Self, out data);
        }

        public JitManagerInfo[] GetJitManagers()
        {
            HResult hr = VTable.GetJitManagerList(Self, 0, null, out int needed);
            if (!hr || needed == 0)
                return Array.Empty<JitManagerInfo>();

            JitManagerInfo[] result = new JitManagerInfo[needed];
            fixed (JitManagerInfo* ptr = result)
            {
                hr = VTable.GetJitManagerList(Self, result.Length, ptr, out needed);
                return hr ? result : Array.Empty<JitManagerInfo>();
            }
        }

        public JitCodeHeapInfo[] GetCodeHeapList(ulong jitManager)
        {
            HResult hr = VTable.GetCodeHeapList(Self, jitManager, 0, null, out int needed);
            if (!hr || needed == 0)
                return Array.Empty<JitCodeHeapInfo>();

            JitCodeHeapInfo[] result = new JitCodeHeapInfo[needed];
            fixed (JitCodeHeapInfo* ptr = result)
            {
                hr = VTable.GetCodeHeapList(Self, jitManager, result.Length, ptr, out needed);
                return hr ? result : Array.Empty<JitCodeHeapInfo>();
            }
        }

        public enum ModuleMapTraverseKind
        {
            TypeDefToMethodTable,
            TypeRefToMethodTable
        }

        public delegate void ModuleMapTraverse(int index, ulong methodTable, IntPtr token);

        public HResult TraverseModuleMap(ModuleMapTraverseKind mt, ulong module, ModuleMapTraverse traverse)
        {
            HResult hr = VTable.TraverseModuleMap(Self, mt, module, Marshal.GetFunctionPointerForDelegate(traverse), IntPtr.Zero);
            GC.KeepAlive(traverse);
            return hr;
        }

        public delegate void LoaderHeapTraverse(ulong address, IntPtr size, int isCurrent);

        public HResult TraverseLoaderHeap(ulong heap, LoaderHeapTraverse callback)
        {
            HResult hr = VTable.TraverseLoaderHeap(Self, heap, Marshal.GetFunctionPointerForDelegate(callback));
            GC.KeepAlive(callback);
            return hr;
        }

        public enum VCSHeapType
        {
            IndcellHeap,
            LookupHeap,
            ResolveHeap,
            DispatchHeap,
            CacheEntryHeap,
            VtableHeap
        }

        public HResult TraverseStubHeap(ulong heap, VCSHeapType type, LoaderHeapTraverse callback)
        {
            HResult hr = VTable.TraverseVirtCallStubHeap(Self, heap, type, Marshal.GetFunctionPointerForDelegate(callback));
            GC.KeepAlive(callback);
            return hr;
        }

        public SOSHandleEnum? EnumerateHandles(params ClrHandleKind[] types)
        {
            fixed (ClrHandleKind* ptr = types)
            {
                HResult hr = VTable.GetHandleEnumForTypes(Self, ptr, types.Length, out IntPtr ptrEnum);
                if (hr)
                {
                    SOSHandleEnum result = new(_library, ptrEnum);
                    int count = result.Release();
                    if (count == 0)
                        throw new InvalidOperationException($"We expected to borrow a reference from GetHandleEnumForTypes, but instead fully released the object!");

                    return result;
                }
            }

            return null;
        }

        public SOSHandleEnum? EnumerateHandles()
        {
            HResult hr = VTable.GetHandleEnum(Self, out IntPtr ptrEnum);
            if (hr)
            {
                SOSHandleEnum result = new(_library, ptrEnum);
                int count = result.Release();
                if (count == 0)
                    throw new InvalidOperationException($"We expected to borrow a reference from GetHandleEnum, but instead fully released the object!");

                return result;
            }

            return null;
        }

        public SOSStackRefEnum? EnumerateStackRefs(uint osThreadId)
        {
            HResult hr = VTable.GetStackReferences(Self, osThreadId, out IntPtr ptrEnum);

            if (hr)
            {
                SOSStackRefEnum result = new(_library, ptrEnum);
                int count = result.Release();
                if (count == 0)
                    throw new InvalidOperationException($"We expected to borrow a reference from GetStackReferences, but instead fully released the object!");

                return result;
            }
            else
            {
                Trace.TraceInformation($"EnumerateStackRefs for OSThreadId:{osThreadId:x} failed with hr={hr}");
            }

            return null;
        }

        public ulong GetMethodDescFromToken(ulong module, int token)
        {
            HResult hr = VTable.GetMethodDescFromToken(Self, module, token, out ClrDataAddress md);
            if (hr)
                return md;

            return 0;
        }
        private delegate HResult DacGetJitManagerInfo(IntPtr self, ClrDataAddress addr, out JitManagerInfo data);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct ISOSDacVTable
    {
        // ThreadStore
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out ThreadStoreData, int> GetThreadStoreData;

        // AppDomains
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out AppDomainStoreData, int> GetAppDomainStoreData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, ClrDataAddress*, out int, int> GetAppDomainList;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out AppDomainData, int> GetAppDomainData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> GetAppDomainName;
        public readonly IntPtr GetDomainFromContext;

        // Assemblies
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, ClrDataAddress*, out int, int> GetAssemblyList;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, ClrDataAddress, out AssemblyData, int> GetAssemblyData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> GetAssemblyName;

        // Modules
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out IntPtr, int> GetModule;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out ModuleData, int> GetModuleData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, SOSDac.ModuleMapTraverseKind, ClrDataAddress, IntPtr, IntPtr, int> TraverseModuleMap;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, ClrDataAddress*, out int, int> GetAssemblyModuleList;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, uint, out ClrDataAddress, int> GetILForModule;

        // Threads

        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out ThreadData, int> GetThreadData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, out ClrDataAddress, int> GetThreadFromThinlockID;
        public readonly IntPtr GetStackLimits;

        // MethodDescs

        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, ulong, out MethodDescData, int, RejitData[]?, out int, int> GetMethodDescData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out ClrDataAddress, int> GetMethodDescPtrFromIP;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> GetMethodDescName;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out ClrDataAddress, int> GetMethodDescPtrFromFrame;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, out ClrDataAddress, int> GetMethodDescFromToken;
        private readonly IntPtr GetMethodDescTransparencyData;

        // JIT Data
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out CodeHeaderData, int> GetCodeHeaderData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, JitManagerInfo*, out int, int> GetJitManagerList;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> GetJitHelperFunctionName;
        private readonly IntPtr GetJumpThunkTarget;

        // ThreadPool

        public readonly delegate* unmanaged[Stdcall]<IntPtr, out ThreadPoolData, int> GetThreadpoolData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out WorkRequestData, int> GetWorkRequestData;
        private readonly IntPtr GetHillClimbingLogEntry;

        // Objects
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out ObjectData, int> GetObjectData;
        public readonly IntPtr GetObjectStringData;
        public readonly IntPtr GetObjectClassName;

        // MethodTable
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> GetMethodTableName;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out MethodTableData, int> GetMethodTableData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, uint, out ClrDataAddress, int> GetMethodTableSlot;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out FieldInfo, int> GetMethodTableFieldData;
        private readonly IntPtr GetMethodTableTransparencyData;

        // EEClass
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out ClrDataAddress, int> GetMethodTableForEEClass;

        // FieldDesc
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out FieldData, int> GetFieldDescData;

        // Frames
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> GetFrameName;

        // PEFiles
        public readonly IntPtr GetPEFileBase;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> GetPEFileName;

        // GC
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out GCInfo, int> GetGCHeapData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, ClrDataAddress*, out int, int> GetGCHeapList; // svr only
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out HeapDetails, int> GetGCHeapDetails; // wks only
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out HeapDetails, int> GetGCHeapStaticData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out SegmentData, int> GetHeapSegmentData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out DacOOMData, int> GetOOMData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out DacOOMData, int> GetOOMStaticData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out DacHeapAnalyzeData, int> GetHeapAnalyzeData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out DacHeapAnalyzeData, int> GetHeapAnalyzeStaticData;

        // DomainLocal
        private readonly IntPtr GetDomainLocalModuleData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, out DomainLocalModuleData, int> GetDomainLocalModuleDataFromAppDomain;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out DomainLocalModuleData, int> GetDomainLocalModuleDataFromModule;

        // ThreadLocal
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, uint, out ThreadLocalModuleData, int> GetThreadLocalModuleData;

        // SyncBlock
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out SyncBlockData, int> GetSyncBlockData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ulong, out SyncBlockCleanupData, int> GetSyncBlockCleanupData;

        // Handles
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out IntPtr, int> GetHandleEnum;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrHandleKind*, int, out IntPtr, int> GetHandleEnumForTypes;
        private readonly IntPtr GetHandleEnumForGC;

        // EH
        private readonly IntPtr TraverseEHInfo;
        private readonly IntPtr GetNestedExceptionData;

        // StressLog
        public readonly IntPtr GetStressLogAddress;

        // Heaps
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, IntPtr, int> TraverseLoaderHeap;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, JitCodeHeapInfo*, out int, int> GetCodeHeapList;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, SOSDac.VCSHeapType, IntPtr, int> TraverseVirtCallStubHeap;

        // Other
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out CommonMethodTables, int> GetUsefulGlobals;
        public readonly IntPtr GetClrWatsonBuckets;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out uint, int> GetTLSIndex;
        public readonly IntPtr GetDacModuleHandle;

        // COM
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out RcwData, int> GetRCWData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, COMInterfacePointerData*, out int, int> GetRCWInterfaces;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out CcwData, int> GetCCWData;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, COMInterfacePointerData*, out int, int> GetCCWInterfaces;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, nint, nint, int> TraverseRCWCleanupList;

        // GC Reference Functions
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int> GetStackReferences;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, int, char*, out int, int> GetRegisterName;
        public readonly IntPtr GetThreadAllocData;
        public readonly IntPtr GetHeapAllocData;

        // For BindingDisplay plugin

        public readonly IntPtr GetFailedAssemblyList;
        public readonly IntPtr GetPrivateBinPaths;
        public readonly IntPtr GetAssemblyLocation;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> GetAppDomainConfigFile;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int, byte*, out int, int> GetApplicationBase;
        public readonly IntPtr GetFailedAssemblyData;
        public readonly IntPtr GetFailedAssemblyLocation;
        public readonly IntPtr GetFailedAssemblyDisplayName;
    }
}