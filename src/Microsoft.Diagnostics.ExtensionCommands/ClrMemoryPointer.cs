using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.DacInterface;
using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    internal class ClrMemoryPointer
    {
        public ulong Address { get; }

        public ClrMemoryKind Kind { get; }

        public ClrMemoryPointer(ulong address, ClrMemoryKind kind)
        {
            Address = address;
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
                    yield return new ClrMemoryPointer(handle.Address, ClrMemoryKind.HandleTable);

                foreach (var mem in sos.GetCodeHeapList(jitMgr.Address))
                    yield return new ClrMemoryPointer(mem.Address, mem.Type switch
                    {
                        CodeHeapType.Loader => ClrMemoryKind.LoaderHeap,
                        CodeHeapType.Host => ClrMemoryKind.Host,
                        _ => ClrMemoryKind.UnknownCodeHeap
                    });

                Console.WriteLine("GC Segments:");
                foreach (var seg in runtime.Heap.Segments)
                {
                    if (seg.CommittedMemory.Length > 0)
                        yield return new ClrMemoryPointer(seg.CommittedMemory.Start, ClrMemoryKind.GCHeapSegment);

                    if (seg.ReservedMemory.Length > 0)
                        yield return new ClrMemoryPointer(seg.ReservedMemory.Start, ClrMemoryKind.GCHeapReserve);
                }

                HashSet<ulong> seen = new();

                List<ClrMemoryPointer> heaps = new();
                if (runtime.SystemDomain is not null)
                    AddAppDomainHeaps(sos, runtime.SystemDomain.Address, heaps);

                if (runtime.SharedDomain is not null)
                    AddAppDomainHeaps(sos, runtime.SharedDomain.Address, heaps);

                foreach (var heap in heaps)
                    if (seen.Add(heap.Address))
                        yield return heap;

                foreach (ClrDataAddress address in sos.GetAppDomainList())
                {
                    heaps.Clear();
                    AddAppDomainHeaps(sos, address, heaps);

                    foreach (var heap in heaps)
                        if (seen.Add(heap.Address))
                            yield return heap;
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

        private static void AddAppDomainHeaps(SOSDac sos, ClrDataAddress address, List<ClrMemoryPointer> heaps)
        {
            if (sos.GetAppDomainData(address, out AppDomainData domain))
            {
                sos.TraverseLoaderHeap(domain.StubHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, ClrMemoryKind.StubHeap)));
                sos.TraverseLoaderHeap(domain.HighFrequencyHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, ClrMemoryKind.HighFrequencyHeap)));
                sos.TraverseLoaderHeap(domain.LowFrequencyHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, ClrMemoryKind.LowFrequencyHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.IndcellHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, ClrMemoryKind.IndcellHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.LookupHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, ClrMemoryKind.LookupHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.ResolveHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, ClrMemoryKind.ResolveHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.DispatchHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, ClrMemoryKind.DispatchHeap)));
                sos.TraverseStubHeap(address, (int)VCSHeapType.CacheEntryHeap, (address, size, isCurrent) => heaps.Add(new ClrMemoryPointer(address, ClrMemoryKind.CacheEntryHeap)));
            }
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
    }
}
