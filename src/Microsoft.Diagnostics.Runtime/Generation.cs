// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// The "generation" of an object.  Note that this is a simulated view
    /// of what a "generation" is.  For example, Large objects are still
    /// considered gen2 for GC collection purposes.
    /// </summary>
    public enum Generation
    {
        /// <summary>
        /// Gen0 objects.  These may reside on an Ephemeral segment, or
        /// on a Gen0 region.
        /// </summary>
        Generation0,

        /// <summary>
        /// Gen1 objects.  These may reside on an Ephemeral segment, or
        /// on a Gen1 region.
        /// </summary>
        Generation1,

        /// <summary>
        /// Gen2 objects.  These may reside on an Ephemeral segment, or
        /// on a Gen2 region.
        /// </summary>
        Generation2,

        /// <summary>
        /// Objects on the Large Object Heap, considered gen2 for collection.
        /// </summary>
        Large,

        /// <summary>
        /// Objects on the Pinned Object Heap, considered gen2 for collection.
        /// </summary>
        Pinned,

        /// <summary>
        /// Frozen objects will never be collected.
        /// </summary>
        Frozen,

        /// <summary>
        /// Unknown object generation.  Could be a bug within ClrMD or a sign
        /// of heap corruption.
        /// </summary>
        Unknown
    }
}