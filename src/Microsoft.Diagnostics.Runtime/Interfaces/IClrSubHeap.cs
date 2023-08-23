// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrSubHeap
    {
        ulong Address { get; }
        ulong Allocated { get; }
        ulong MarkArray { get; }
        MemoryRange AllocationContext { get; }
        bool HasBackgroundGC { get; }
        bool HasPinnedObjectHeap { get; }
        bool HasRegions { get; }
        IClrHeap Heap { get; }
        int Index { get; }
        ImmutableArray<IClrSegment> Segments { get; }
        MemoryRange FinalizerQueueRoots { get; }
        MemoryRange FinalizerQueueObjects { get; }
        ulong SavedSweepEphemeralSegment { get; }
        ulong SavedSweepEphemeralStart { get; }
        ImmutableArray<IClrGenerationData> GenerationTable { get; }
        ulong EphemeralHeapSegment { get; }
        ulong LowestAddress { get; }
        ulong HighestAddress { get; }
        ulong CardTable { get; }
        IClrOutOfMemoryInfo? OomInfo { get; }
        MemoryRange InternalRootArray { get; }
        ImmutableArray<MemoryRange> GenerationalFinalizableObjects { get; }
    }
}
