// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// The kind of native heap.
    /// </summary>
    public enum NativeHeapKind
    {
        /// <summary>
        /// This region of memory is a heap that was allocated by CLR, but we didn't have
        /// enough information to tell which kind of heap.
        /// </summary>
        Unknown,

        /// <summary>
        /// Indirection cells.
        /// </summary>
        IndirectionCellHeap,

        /// <summary>
        /// Lookup stubs.
        /// </summary>
        LookupHeap,

        /// <summary>
        /// Resolve stubs.
        /// </summary>
        ResolveHeap,

        /// <summary>
        /// Dispatch stubs.
        /// </summary>
        DispatchHeap,

        /// <summary>
        /// Resolve cache element entries.
        /// </summary>
        CacheEntryHeap,

        /// <summary>
        /// Vtable-based jump stubs.
        /// </summary>
        VtableHeap,

        /// <summary>
        /// Loader Codeheaps.
        /// </summary>
        LoaderCodeHeap,

        /// <summary>
        /// Host Codeheaps.
        /// </summary>
        HostCodeHeap,

        /// <summary>
        /// Stub heap.
        /// </summary>
        StubHeap,

        /// <summary>
        /// High frequency heap.
        /// </summary>
        HighFrequencyHeap,

        /// <summary>
        /// Low frequency heap.
        /// </summary>
        LowFrequencyHeap,

        /// <summary>
        /// Executable heap.
        /// </summary>
        ExecutableHeap,

        /// <summary>
        /// FixupPrecodeHeap
        /// </summary>
        FixupPrecodeHeap,

        /// <summary>
        /// NewStubPrecodeHeap
        /// </summary>
        NewStubPrecodeHeap,

        /// <summary>
        /// ThunkHeap
        /// </summary>
        ThunkHeap,

        /// <summary>
        /// GC Handle table.
        /// </summary>
        HandleTable,

        /// <summary>
        /// GC Bookkeeping regions
        /// </summary>
        GCBookkeeping,

        /// <summary>
        /// GC segments which is currently unused (but still allocated).
        /// </summary>
        GCFreeRegion,

        GCFreeGlobalHugeRegion,
        GCFreeGlobalRegion,
        GCFreeSohSegment,
        GCFreeUohSegment,
    }
}