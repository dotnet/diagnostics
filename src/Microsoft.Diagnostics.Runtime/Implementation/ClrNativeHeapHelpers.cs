// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Utilities;
using static Microsoft.Diagnostics.Runtime.DacInterface.SOSDac13;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ClrNativeHeapHelpers : IClrNativeHeapHelpers
    {
        private NativeHeapKind[]? _heapNativeTypes;
        private readonly ClrInfo _clrInfo;
        private readonly SOSDac _sos;
        private readonly ISOSDac13? _sos13;
        private readonly IDataReader _dataReader;

        public ClrNativeHeapHelpers(ClrInfo clrInfo, SOSDac sos, ISOSDac13? sos13, IDataReader dataReader)
        {
            _clrInfo = clrInfo;
            _sos = sos;
            _sos13 = sos13;
            _dataReader = dataReader;
        }

        private NativeHeapKind[] GetNativeHeaps()
        {
            if (_heapNativeTypes is not null)
                return _heapNativeTypes;

            if (_sos13 is null)
                return _heapNativeTypes = Array.Empty<NativeHeapKind>();

            return _heapNativeTypes = _sos13.GetLoaderAllocatorHeapNames().Select(r => r switch {
                "LowFrequencyHeap" => NativeHeapKind.LowFrequencyHeap,
                "HighFrequencyHeap" => NativeHeapKind.HighFrequencyHeap,
                "StubHeap" => NativeHeapKind.StubHeap,
                "ExecutableHeap" => NativeHeapKind.ExecutableHeap,
                "FixupPrecodeHeap" => NativeHeapKind.FixupPrecodeHeap,
                "NewStubPrecodeHeap" => NativeHeapKind.NewStubPrecodeHeap,
                "IndcellHeap" => NativeHeapKind.IndirectionCellHeap,
                "LookupHeap" => NativeHeapKind.LookupHeap,
                "ResolveHeap" => NativeHeapKind.ResolveHeap,
                "DispatchHeap" => NativeHeapKind.DispatchHeap,
                "CacheEntryHeap" => NativeHeapKind.CacheEntryHeap,
                "VtableHeap" => NativeHeapKind.VtableHeap,
                _ => NativeHeapKind.Unknown
            }).ToArray();
        }

        public IEnumerable<ClrNativeHeapInfo> EnumerateNativeHeaps(ClrJitManager jitManager)
        {
            List<ClrNativeHeapInfo>? codeLoaderHeaps = null;

            foreach (JitCodeHeapInfo mem in _sos.GetCodeHeapList(jitManager.Address))
            {
                if (mem.Kind == CodeHeapKind.Loader)
                {
                    codeLoaderHeaps?.Clear();

                    foreach (ClrNativeHeapInfo heap in LegacyEnumerateLoaderAllocatorHeaps(mem.Address, LoaderHeapKind.LoaderHeapKindExplicitControl, NativeHeapKind.LoaderCodeHeap))
                        yield return heap;
                }
                else if (mem.Kind == CodeHeapKind.Host)
                {
                    yield return new ClrNativeHeapInfo(new(mem.Address, mem.CurrentAddress), NativeHeapKind.HostCodeHeap, ClrNativeHeapState.Active);
                }
                else
                {
                    yield return new ClrNativeHeapInfo(new(mem.Address, mem.Address), NativeHeapKind.Unknown, ClrNativeHeapState.None);
                }
            }
        }

        public IEnumerable<ClrNativeHeapInfo> EnumerateNativeHeaps(ClrAppDomain domain)
        {
            if (domain is null)
                yield break;

            ulong loaderAllocator;
            if (_sos13 is not null
                && (loaderAllocator = _sos13.GetDomainLoaderAllocator(domain.Address)) != 0
                && GetNativeHeaps().Length > 0)
            {
                foreach (ClrNativeHeapInfo heap in EnumerateLoaderAllocatorNativeHeaps(loaderAllocator))
                    yield return heap;
            }
            else if (_sos.GetAppDomainData(domain.Address, out AppDomainData data))
            {
                foreach (ClrNativeHeapInfo heapInfo in LegacyEnumerateLoaderAllocatorHeaps(data.StubHeap, LoaderHeapKind.LoaderHeapKindNormal, NativeHeapKind.StubHeap))
                    yield return heapInfo;

                foreach (ClrNativeHeapInfo heapInfo in LegacyEnumerateLoaderAllocatorHeaps(data.HighFrequencyHeap, LoaderHeapKind.LoaderHeapKindNormal, NativeHeapKind.HighFrequencyHeap))
                    yield return heapInfo;

                foreach (ClrNativeHeapInfo heapInfo in LegacyEnumerateLoaderAllocatorHeaps(data.LowFrequencyHeap, LoaderHeapKind.LoaderHeapKindNormal, NativeHeapKind.LowFrequencyHeap))
                    yield return heapInfo;

                foreach (ClrNativeHeapInfo heapInfo in LegacyEnumerateStubHeaps(domain))
                    yield return heapInfo;
            }
        }

        public IEnumerable<ClrNativeHeapInfo> EnumerateLoaderAllocatorNativeHeaps(ulong loaderAllocator)
        {
            NativeHeapKind[] heapNativeTypes;
            if (loaderAllocator == 0
                || _sos13 is null
                || (heapNativeTypes = GetNativeHeaps()).Length == 0)
            {
                yield break;
            }

            List<ClrNativeHeapInfo>? result = null;

            (ClrDataAddress Address, LoaderHeapKind Kind)[] heaps = _sos13.GetLoaderAllocatorHeaps(loaderAllocator);
            for (int i = 0; i < heaps.Length; i++)
            {
                HResult hr = _sos13.TraverseLoaderHeap(heaps[i].Address, heaps[i].Kind, (address, size, current) => {
                    result ??= new(16);
                    result.Add(new(MemoryRange.CreateFromLength(address, SanitizeSize(size)), heapNativeTypes[i], current != 0 ? ClrNativeHeapState.Active : ClrNativeHeapState.Inactive));
                });

                if (hr && result is not null)
                {
                    foreach (ClrNativeHeapInfo info in result)
                        yield return info;
                }

                result?.Clear();
            }
        }

        private IEnumerable<ClrNativeHeapInfo> LegacyEnumerateStubHeaps(ClrAppDomain domain)
        {
            List<ClrNativeHeapInfo> result = new(16);

            TraverseOneStubKind(domain, result, SOSDac.VCSHeapType.IndcellHeap, NativeHeapKind.IndirectionCellHeap);
            foreach (ClrNativeHeapInfo heap in result)
                yield return heap;

            TraverseOneStubKind(domain, result, SOSDac.VCSHeapType.LookupHeap, NativeHeapKind.LookupHeap);
            foreach (ClrNativeHeapInfo heap in result)
                yield return heap;

            TraverseOneStubKind(domain, result, SOSDac.VCSHeapType.ResolveHeap, NativeHeapKind.ResolveHeap);
            foreach (ClrNativeHeapInfo heap in result)
                yield return heap;

            TraverseOneStubKind(domain, result, SOSDac.VCSHeapType.DispatchHeap, NativeHeapKind.DispatchHeap);
            foreach (ClrNativeHeapInfo heap in result)
                yield return heap;

            TraverseOneStubKind(domain, result, SOSDac.VCSHeapType.CacheEntryHeap, NativeHeapKind.CacheEntryHeap);
            foreach (ClrNativeHeapInfo heap in result)
                yield return heap;

            TraverseOneStubKind(domain, result, SOSDac.VCSHeapType.VtableHeap, NativeHeapKind.VtableHeap);
            foreach (ClrNativeHeapInfo heap in result)
                yield return heap;
        }

        private void TraverseOneStubKind(ClrAppDomain domain, List<ClrNativeHeapInfo> result, SOSDac.VCSHeapType vcsType, NativeHeapKind heapKind)
        {
            result.Clear();
            HResult hr = _sos.TraverseStubHeap(domain.Address, vcsType, (address, size, current) => {
                result.Add(new(MemoryRange.CreateFromLength(address, SanitizeSize(size)), heapKind, current != 0 ? ClrNativeHeapState.Active : ClrNativeHeapState.Inactive));
            });

            if (!hr)
                result.Clear();
        }

        private IEnumerable<ClrNativeHeapInfo> LegacyEnumerateLoaderAllocatorHeaps(ulong loaderHeap, LoaderHeapKind loaderHeapKind, NativeHeapKind nativeHeapKind)
        {
            if (loaderHeap != 0)
            {
                List<ClrNativeHeapInfo>? result = null;

                // The basic ISOSDacInterface doesn't understand the difference between the different kinds of runtime
                // loader heaps.  We have to adjust certain loader heap kinds based on the version of dac we are
                // targeting.  This includes .Net 7, and .Net 8 before ISOSDacInterface13 was implemented.  Additionally,
                // we don't know the version info for a lot of versions of single-file compilation.  In all of those
                // cases, we need to adjust the pointer.

                bool normalNeedsAdjustment = false;
                if (_clrInfo.Flavor == ClrFlavor.Core)
                {
                    int versionMajor = _clrInfo.Version.Major;
                    normalNeedsAdjustment = versionMajor == 7 || (versionMajor == 8 && _sos13 is null) || versionMajor == 0;
                }

                ulong fixedHeapAddress = FixupHeapAddress(loaderHeap, loaderHeapKind, normalNeedsAdjustment);

                HResult hr = _sos.TraverseLoaderHeap(fixedHeapAddress, (address, size, current) => {
                    result ??= new(8);
                    result.Add(new(MemoryRange.CreateFromLength(address, SanitizeSize(size)), nativeHeapKind, current != 0 ? ClrNativeHeapState.Active : ClrNativeHeapState.Inactive));
                });

                if (result is not null && result.Count > 0 && normalNeedsAdjustment)
                {
                    // If we adjusted the pointer and we can't read the resulting addresses, try again with the
                    // opposite setting.
                    byte[] buffer = new byte[1];

                    if (result.Any(entry => _dataReader.Read(entry.MemoryRange.Start, buffer) == 0))
                    {
                        result.Clear();
                        fixedHeapAddress = FixupHeapAddress(loaderHeap, loaderHeapKind, !normalNeedsAdjustment);

                        hr = _sos.TraverseLoaderHeap(fixedHeapAddress, (address, size, current) => {
                            result ??= new(8);
                            result.Add(new(MemoryRange.CreateFromLength(address, SanitizeSize(size)), nativeHeapKind, current != 0 ? ClrNativeHeapState.Active : ClrNativeHeapState.Inactive));
                        });
                    }
                }

                // If TraverseLoaderHeap returns a failing HRESULT, it means that it encountered a bad block.
                // This likely means that loaderHeap points to bad memory and we should ignore this entire
                // enumeration.
                if (hr && result != null)
                    return result;
            }

            return Enumerable.Empty<ClrNativeHeapInfo>();
        }

        private ulong FixupHeapAddress(ulong loaderHeap, LoaderHeapKind loaderHeapKind, bool normalNeedsAdjustment)
        {
            if (normalNeedsAdjustment)
            {
                if (loaderHeapKind == LoaderHeapKind.LoaderHeapKindNormal)
                    loaderHeap += (uint)_dataReader.PointerSize;
            }
            else
            {
                if (loaderHeapKind == LoaderHeapKind.LoaderHeapKindExplicitControl)
                    loaderHeap -= (uint)_dataReader.PointerSize;
            }

            return loaderHeap;
        }

        public IEnumerable<ClrNativeHeapInfo> EnumerateThunkHeaps(ulong thunkHeapAddress)
        {
            if (thunkHeapAddress != 0)
            {
                List<ClrNativeHeapInfo>? heaps = null;
                HResult hr = TraverseLoaderHeap(thunkHeapAddress, LoaderHeapKind.LoaderHeapKindNormal, (address, size, current) => {
                    heaps ??= new(16);
                    heaps.Add(new(MemoryRange.CreateFromLength(address, SanitizeSize(size)), NativeHeapKind.ThunkHeap, current != 0 ? ClrNativeHeapState.Active : ClrNativeHeapState.Inactive));
                });

                if (hr && heaps is not null && heaps.Count > 0)
                    return heaps;
            }

            return Enumerable.Empty<ClrNativeHeapInfo>();
        }

        internal static ulong SanitizeSize(nint size)
        {
            // If TraverseHeap returns a negative size or a size that's too large, we'll treat
            // this as not having size info.  This shouldn't happen in practice.
            if (size is < 0 or > int.MaxValue)
                return 0;

            return (ulong)size;
        }

        private HResult TraverseLoaderHeap(ulong address, LoaderHeapKind kind, SOSDac.LoaderHeapTraverse callback)
            => TraverseLoaderHeap(_clrInfo, _sos, _sos13, address, kind, (uint)_dataReader.PointerSize, callback);

        private static HResult TraverseLoaderHeap(ClrInfo clrInfo, SOSDac sos, ISOSDac13? sos13, ulong address, LoaderHeapKind kind, uint pointerSize, SOSDac.LoaderHeapTraverse callback)
        {
            if (address == 0)
                return HResult.E_INVALIDARG;

            HResult hr;
            if (sos13 is not null)
            {
                // ISOSDacInterface13 understands how to walk LoaderHeaps properly, but it's only implemented in
                // .Net 8 and beyond.

                hr = sos13.TraverseLoaderHeap(address, kind, callback);
            }
            else if (clrInfo.Flavor == ClrFlavor.Core && clrInfo.Version.Major == 7)
            {
                // See note below, .Net 7 inverts the logic that everything else uses.

                if (kind == LoaderHeapKind.LoaderHeapKindNormal)
                    address += pointerSize;

                hr = sos.TraverseLoaderHeap(address, callback);
            }
            else
            {
                // The basic ISOSDacInterface doesn't understand the difference between the different kinds of runtime
                // loader heaps.  If the heap is an ExplicitControlLoaderHeap then it doesn't have a vtable but it will
                // be treated as a LoaderHeap, which has the same layout aside from the fact that it DOES have a vtable.
                // So, if we are enumerating an ExplictControl we need to move the address back by one pointer so that
                // the enumeration code will work properly.

                if (kind == LoaderHeapKind.LoaderHeapKindExplicitControl)
                    address -= pointerSize;

                hr = sos.TraverseLoaderHeap(address, callback);
            }

            GC.KeepAlive(callback);
            return hr;
        }
    }
}