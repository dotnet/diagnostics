// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// The managed heap in CLR is made up of a number of logical "heaps".  When using
    /// Workstation GC, the managed heap has only one logical "heap".  When using Server GC,
    /// there can be many of them.  This class tracks information about logical heaps.
    /// </summary>
    public class ClrSubHeap : IClrSubHeap
    {
        internal ClrSubHeap(IClrHeapHelpers helpers, ClrHeap clrHeap, int index, ulong address, in HeapDetails heap, IEnumerable<GenerationData> genData, IEnumerable<ulong> finalizationPointers)
        {
            Heap = clrHeap;
            Address = address;
            Index = index;
            Allocated = heap.Allocated;
            MarkArray = heap.MarkArray;
            State = (GCState)(ulong)heap.CurrentGCState;
            CurrentSweepPosition = heap.NextSweepObj;
            SavedSweepEphemeralSegment = heap.SavedSweepEphemeralSeg;
            SavedSweepEphemeralStart = heap.SavedSweepEphemeralStart;
            BackgroundSavedLowestAddress = heap.BackgroundSavedLowestAddress;
            BackgroundSavedHighestAddress = heap.BackgroundSavedHighestAddress;
            EphemeralHeapSegment = heap.EphemeralHeapSegment;
            LowestAddress = heap.LowestAddress;
            HighestAddress = heap.HighestAddress;
            CardTable = heap.CardTable;

            GenerationTable = genData.Select(data => new ClrGenerationData(data)).ToImmutableArray();
            FinalizationPointers = finalizationPointers.ToImmutableArray();

            if (FinalizationPointers.Length == 6)
            {
                // Pre-Regions
                FinalizerQueueObjects = new(FinalizationPointers[0], FinalizationPointers[5]);
                FinalizerQueueRoots = new(FinalizationPointers[3], FinalizationPointers[5]);
            }
            else
            {
                // GC-Regions
                FinalizerQueueObjects = new(FinalizationPointers[0], FinalizationPointers[6]);
                FinalizerQueueRoots = new(FinalizationPointers[4], FinalizationPointers[6]);
            }

            // These are stored in reverse order
            ImmutableArray<MemoryRange>.Builder builder = ImmutableArray.CreateBuilder<MemoryRange>(3);
            builder.Add(CreateMemoryRangeCarefully(FinalizationPointers[2], FinalizationPointers[3]));
            builder.Add(CreateMemoryRangeCarefully(FinalizationPointers[1], FinalizationPointers[2]));
            builder.Add(CreateMemoryRangeCarefully(FinalizationPointers[0], FinalizationPointers[1]));
            GenerationalFinalizableObjects = builder.MoveToImmutable();

            HasRegions = GenerationTable.Length >= 2 && GenerationTable[0].StartSegment != GenerationTable[1].StartSegment;
            HasPinnedObjectHeap = GenerationTable.Length > 4;

            AllocationContext = new MemoryRange(heap.EphemeralAllocContextPtr, heap.EphemeralAllocContextLimit);

            Segments = helpers.EnumerateSegments(this).ToImmutableArray();
        }

        private static MemoryRange CreateMemoryRangeCarefully(ulong start, ulong stop) => start <= stop ? new(start, stop) : default;

        public ClrHeap Heap { get; }
        IClrHeap IClrSubHeap.Heap => Heap;

        public ImmutableArray<ClrSegment> Segments { get; }

        public MemoryRange FinalizerQueueRoots { get; }
        public MemoryRange FinalizerQueueObjects { get; }
        public ImmutableArray<MemoryRange> GenerationalFinalizableObjects { get; }
        public MemoryRange AllocationContext { get; }

        public int Index { get; }

        public bool HasPinnedObjectHeap { get; }
        public bool HasRegions { get; }
        public bool HasBackgroundGC { get; }

        public ulong Address { get; }

        public ulong Allocated { get; }
        public ulong MarkArray { get; }

        internal GCState State { get; }

        internal ulong CurrentSweepPosition { get; }
        public ulong SavedSweepEphemeralSegment { get; }
        public ulong SavedSweepEphemeralStart { get; }
        internal ulong BackgroundSavedLowestAddress { get; }
        internal ulong BackgroundSavedHighestAddress { get; }

        public ImmutableArray<ClrGenerationData> GenerationTable { get; }
        public ulong EphemeralHeapSegment { get; }

        public ImmutableArray<ulong> FinalizationPointers { get; }

        public ulong LowestAddress { get; }
        public ulong HighestAddress { get; }
        public ulong CardTable { get; }

        public ClrOutOfMemoryInfo? OomInfo => Heap.Helpers.GetOOMInfo(this);

        public MemoryRange InternalRootArray => Heap.Helpers.GetInternalRootArray(this);

        ImmutableArray<IClrSegment> IClrSubHeap.Segments => Segments.CastArray<IClrSegment>();

        IClrOutOfMemoryInfo? IClrSubHeap.OomInfo => OomInfo;

        ImmutableArray<IClrGenerationData> IClrSubHeap.GenerationTable => GenerationTable.CastArray<IClrGenerationData>();

        internal enum GCState
        {
            Marking,
            Planning,
            Free
        }
    }
}
