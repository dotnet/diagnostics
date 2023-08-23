// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Types of CLR handles.
    /// </summary>
    public enum ClrHandleKind
    {
        /// <summary>
        /// Weak, short lived handle.
        /// </summary>
        WeakShort = 0,

        /// <summary>
        /// Weak, long lived handle.
        /// </summary>
        WeakLong = 1,

        /// <summary>
        /// Strong handle.
        /// </summary>
        Strong = 2,

        /// <summary>
        /// Strong handle, prevents relocation of target object.
        /// </summary>
        Pinned = 3,

        /// <summary>
        /// RefCounted handle (strong when the reference count is greater than 0).
        /// </summary>
        RefCounted = 5,

        /// <summary>
        /// A weak handle which may keep its "secondary" object alive if the "target" object is also alive.
        /// </summary>
        Dependent = 6,

        /// <summary>
        /// A strong, pinned handle (keeps the target object from being relocated), used for async IO operations.
        /// </summary>
        AsyncPinned = 7,

        /// <summary>
        /// Strong handle used internally for book keeping.
        /// </summary>
        SizedRef = 8,

        /// <summary>
        /// Weak WinRT handle.
        /// </summary>
        WeakWinRT = 9,
    }
}