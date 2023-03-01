// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.DacInterface;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    internal sealed class ClrMemoryPointer
    {
        public ulong Address { get; }

        /// <summary>
        /// Size may be 0 if we do not know the size of a segment.
        /// </summary>
        public ulong Size { get; }

        public ClrMemoryKind Kind { get; }

        public ClrMemoryPointer(ulong address, ClrMemoryKind kind)
        {
            Address = address;
            Kind = kind;
        }

        public ClrMemoryPointer(ulong address, ulong length, ClrMemoryKind kind)
        {
            Address = address;
            Size = length;
            Kind = kind;
        }

        /// <summary>
        /// Enumerates pointers to various CLR heaps in memory.
        /// </summary>
        public static IEnumerable<ClrMemoryPointer> EnumerateClrMemoryAddresses(ClrRuntime runtime)
        {
            SOSDac sos = runtime.DacLibrary.SOSDacInterface;
            foreach (JitManagerInfo jitMgr in sos.GetJitManagers())
            {
                foreach (var handle in runtime.EnumerateHandles())
                {
                    yield return new ClrMemoryPointer(handle.Address, ClrMemoryKind.HandleTable);
                }

                List<ClrMemoryPointer> heaps = new();
                foreach (var mem in sos.GetCodeHeapList(jitMgr.Address))
                {
                    if (mem.Type == CodeHeapType.Loader)
                    {
                        sos.TraverseLoaderHeap(mem.Address, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, GetSize(size), ClrMemoryKind.CodeHeap)));
                    }
                    else
                    {
                        yield return new ClrMemoryPointer(mem.Address, mem.Type switch
                        {
                            CodeHeapType.Loader => ClrMemoryKind.LoaderHeap,
                            CodeHeapType.Host => ClrMemoryKind.Host,
                            _ => ClrMemoryKind.UnknownCodeHeap
                        });
                    }
                }

                foreach (ClrMemoryPointer ptr in heaps)
                {
                    yield return ptr;
                }

                heaps.Clear();

                foreach (var seg in runtime.Heap.Segments)
                {
                    if (seg.CommittedMemory.Length > 0)
                    {
                        yield return new ClrMemoryPointer(seg.CommittedMemory.Start, seg.CommittedMemory.Length, ClrMemoryKind.GCHeapSegment);
                    }

                    if (seg.ReservedMemory.Length > 0)
                    {
                        yield return new ClrMemoryPointer(seg.ReservedMemory.Start, seg.ReservedMemory.Length, ClrMemoryKind.GCHeapReserve);
                    }
                }

                HashSet<ulong> seen = new();

                if (runtime.SystemDomain is not null)
                {
                    AddAppDomainHeaps(runtime, sos, runtime.SystemDomain.Address, heaps);
                }

                if (runtime.SharedDomain is not null)
                {
                    AddAppDomainHeaps(runtime, sos, runtime.SharedDomain.Address, heaps);
                }

                foreach (var heap in heaps)
                {
                    if (seen.Add(heap.Address))
                    {
                        yield return heap;
                    }
                }

                foreach (ClrDataAddress address in sos.GetAppDomainList())
                {
                    heaps.Clear();
                    AddAppDomainHeaps(runtime, sos, address, heaps);

                    foreach (var heap in heaps)
                    {
                        if (seen.Add(heap.Address))
                        {
                            yield return heap;
                        }
                    }
                }
            }
        }

        private enum VCSHeapType
        {
            IndcellHeap,
            LookupHeap,
            ResolveHeap,
            DispatchHeap,
            CacheEntryHeap
        }

        private static void AddAppDomainHeaps(ClrRuntime runtime, SOSDac sos, ClrDataAddress address, List<ClrMemoryPointer> heaps)
        {
            if (sos.GetAppDomainData(address, out AppDomainData domain))
            {
                sos.TraverseLoaderHeap(AdjustAddress(runtime, domain.StubHeap), (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, GetSize(size), ClrMemoryKind.StubHeap)));
                sos.TraverseLoaderHeap(AdjustAddress(runtime, domain.HighFrequencyHeap), (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, GetSize(size), ClrMemoryKind.HighFrequencyHeap)));
                sos.TraverseLoaderHeap(AdjustAddress(runtime, domain.LowFrequencyHeap), (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, GetSize(size), ClrMemoryKind.LowFrequencyHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.IndcellHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, GetSize(size), ClrMemoryKind.IndcellHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.LookupHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, GetSize(size), ClrMemoryKind.LookupHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.ResolveHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, GetSize(size), ClrMemoryKind.ResolveHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.DispatchHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, GetSize(size), ClrMemoryKind.DispatchHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.CacheEntryHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, GetSize(size), ClrMemoryKind.CacheEntryHeap)));
            }
        }

        private static ulong AdjustAddress(ClrRuntime runtime, ulong address)
        {
            // .Net 7 has an issue where it changed the kind of LoaderHeap it expects in TraverseLoaderHeap.
            // On this runtime, we will shift the pointer forward to skip the vtable, as the type of heap
            // the dac expects to walk has the same layout of LoaderHeap, except for the vtable.
            if (runtime.ClrInfo.Flavor == ClrFlavor.Core && runtime.ClrInfo.Version.Major == 7)
            {
                return address + (uint)runtime.DataTarget.DataReader.PointerSize;
            }

            return address;
        }

        private static ulong GetSize(nint size)
        {
            // Some sanity checks on size in case we get bad data in the future.
            if (size <= 0 || size > int.MaxValue)
            {
                return 0;
            }

            return (ulong)size;
        }
    }

    internal enum ClrMemoryKind
    {
        None,
        LoaderHeap,
        Host,
        UnknownCodeHeap,
        GCHeapSegment,
        GCHeapReserve,
        StubHeap,
        HighFrequencyHeap,
        LowFrequencyHeap,
        IndcellHeap,
        LookupHeap,
        ResolveHeap,
        DispatchHeap,
        CacheEntryHeap,
        HandleTable,
        CodeHeap,
    }
}
