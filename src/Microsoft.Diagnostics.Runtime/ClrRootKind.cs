// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// The type of GCRoot that a ClrRoot represents.
    /// </summary>
    public enum ClrRootKind
    {
        /// <summary>
        /// This is not a gc root.  This will not be enumerated out of ClrHeap.EnumerateRoots, but
        /// could be seen when using ClrRuntime.EnumerateHandles.
        /// </summary>
        None = 0,

        /// <summary>
        /// The root comes from the finalizer queue.
        /// </summary>
        FinalizerQueue = 1,

        /// <summary>
        /// The root is a strong handle.
        /// </summary>
        StrongHandle = 2,

        /// <summary>
        /// The root is a strong pinned handle.
        /// </summary>
        PinnedHandle = 3,

        /// <summary>
        /// The root is on the stack of a thread.  This is usually a is a local variable
        /// (or compiler generated temporary variable).
        /// </summary>
        Stack = 4,

        /// <summary>
        /// The root is a ref counted handle.
        /// </summary>
        RefCountedHandle = 5,

        /// <summary>
        /// The root is an async IO (strong) pinned handle.
        /// </summary>
        AsyncPinnedHandle = 7,

        /// <summary>
        /// The root is a SizedRef handle.
        /// </summary>
        SizedRefHandle = 8,
    }
}
