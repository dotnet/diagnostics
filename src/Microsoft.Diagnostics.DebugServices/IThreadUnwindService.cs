// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Thread unwind service
    /// </summary>
    public interface IThreadUnwindService
    {
        /// <summary>
        /// Unwind thread stack 
        /// </summary>
        /// <param name="threadId">thread id to unwind</param>
        /// <param name="contextSize">register context size</param>
        /// <param name="context">register context</param>
        /// <returns>HRESULT</returns>
        int Unwind(uint threadId, uint contextSize, byte[] context);
    }
}
