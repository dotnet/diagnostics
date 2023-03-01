// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Thread unwind service
    /// </summary>
    public interface IThreadUnwindService
    {
        /// <summary>
        /// Unwind the thread's stack from the provided context by one frame.
        /// The input will be the context of a frame to unwind, and upon successful
        /// return the context will be modified to reflect the parent frame's context.
        /// </summary>
        /// <param name="threadId">thread id to unwind</param>
        /// <param name="contextSize">register context size</param>
        /// <param name="context">On input, the frame to unwind. On return, the context of the next frame</param>
        /// <returns>HRESULT</returns>
        int Unwind(uint threadId, uint contextSize, byte[] context);
    }
}
