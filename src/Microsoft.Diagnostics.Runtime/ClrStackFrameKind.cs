// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// The type of frame the ClrStackFrame represents.
    /// </summary>
    public enum ClrStackFrameKind
    {
        /// <summary>
        /// Indicates this stack frame is unknown
        /// </summary>
        Unknown = -1,

        /// <summary>
        /// Indicates this stack frame is a standard managed method.
        /// </summary>
        ManagedMethod = 0,

        /// <summary>
        /// Indicates this stack frame is a special stack marker that the CLR leaves on the stack.
        /// Note that the <see cref="ClrStackFrame"/> may still have a <see cref="ClrMethod"/> associated with the marker.
        /// </summary>
        Runtime = 1
    }
}