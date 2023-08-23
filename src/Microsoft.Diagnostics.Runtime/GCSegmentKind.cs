// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// The kind of GC Segment or region.
    /// </summary>
    public enum GCSegmentKind
    {
        /// <summary>
        /// This "segment" is actually a GC Gen0 region.  This is only enumerated
        /// when the GC regions feature is present in the target CLR.
        /// </summary>
        Generation0,

        /// <summary>
        /// This "segment" is actually a GC Gen1 region.  This is only enumerated
        /// when the GC regions feature is present in the target CLR.
        /// </summary>
        Generation1,

        /// <summary>
        /// This segment contains only Gen2 objects.  This may be a segment or
        /// region.
        /// </summary>
        Generation2,

        /// <summary>
        /// A large object segment.  Objects here are above a certain size (usually
        /// 85,000 bytes) and all objects here are pinned.
        /// </summary>
        Large,

        /// <summary>
        /// Pinned object segment.  All objects here are pinned.
        /// </summary>
        Pinned,

        /// <summary>
        /// This segment is frozen, meaning it is both pinned and no objects will
        /// ever be collected.
        /// </summary>
        Frozen,

        /// <summary>
        /// An Ephemeral segment is one which has Gen0, Gen1, and Gen2 sections.
        /// </summary>
        Ephemeral,
    }
}