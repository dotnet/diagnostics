// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Native or managed stack
    /// </summary>
    public interface IStack
    {
        /// <summary>
        /// Number of total stack frames
        /// </summary>
        int FrameCount { get; }

        /// <summary>
        /// Get an individual stack frame
        /// </summary>
        /// <param name="index">stack frame index</param>
        /// <returns>frame</returns>
        /// <exception cref="ArgumentOutOfRangeException">invalid index</exception>
        IStackFrame GetStackFrame(int index);
    }
}
