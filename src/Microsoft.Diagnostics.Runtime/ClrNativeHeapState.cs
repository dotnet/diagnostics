// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Additional info about CLR native heaps.
    /// </summary>
    public enum ClrNativeHeapState
    {
        /// <summary>
        /// No additional info.
        /// </summary>
        None,

        /// <summary>
        /// Committed memory.
        /// </summary>
        Active,

        /// <summary>
        /// Memory which is no longer the active block in a heap.
        /// For example, some CLR allocators incrementally commit new blocks of memory after it
        /// has filled the current block of memory, then never go back and attempt to add more
        /// data to those previous blocks.  The previous blocks are "inactive".
        /// </summary>
        Inactive,

        /// <summary>
        /// This region of memory contains the bounds of memory where the heap or allocator will
        /// exclusively allocate memory within.  All memory within this memory range is a part of
        /// this native heap.
        /// </summary>
        RegionOfRegions,
    }
}