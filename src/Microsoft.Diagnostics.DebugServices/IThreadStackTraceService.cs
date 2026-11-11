// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Provides stack frames reported by the debugger.
    /// </summary>
    public interface IThreadStackTraceService
    {
        /// <summary>
        /// Gets the debugger stack for the specified operating system thread.
        /// </summary>
        /// <param name="threadId">Operating system thread identifier.</param>
        /// <param name="maxFrames">Maximum number of frames to return.</param>
        /// <returns>The stack in unwind order, with zero for unavailable stack pointers.</returns>
        /// <remarks>Frame ModuleBase and GetMethodName are not implemented.</remarks>
        IStack GetDebuggerStackTrace(uint threadId, int maxFrames);
    }
}
