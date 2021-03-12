// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Provides thread and register info and values
    /// </summary>
    public interface IThreadService
    {
        /// <summary>
        /// Details on all the supported registers
        /// </summary>
        IEnumerable<RegisterInfo> Registers { get; }

        /// <summary>
        /// The instruction pointer register index
        /// </summary>
        int InstructionPointerIndex { get; }

        /// <summary>
        /// The frame pointer register index
        /// </summary>
        int FramePointerIndex { get; }

        /// <summary>
        /// The stack pointer register index
        /// </summary>
        int StackPointerIndex { get; }

        /// <summary>
        /// Return the register index for the register name
        /// </summary>
        /// <param name="name">register name</param>
        /// <param name="registerIndex">returns register index or -1</param>
        /// <returns>true if name found</returns>
        bool TryGetRegisterIndexByName(string name, out int registerIndex);

        /// <summary>
        /// Returns the register info (name, offset, size, etc).
        /// </summary>
        /// <param name="registerIndex">register index</param>
        /// <param name="info">RegisterInfo</param>
        /// <returns>true if index found</returns>
        bool TryGetRegisterInfo(int registerIndex, out RegisterInfo info);

        /// <summary>
        /// Current OS thread Id
        /// </summary>
        uint? CurrentThreadId { get; set; }

        /// <summary>
        /// Enumerate all the native threads
        /// </summary>
        /// <returns>Get info for all the threads</returns>
        IEnumerable<IThread> EnumerateThreads();

        /// <summary>
        /// Get the thread info from the thread index
        /// </summary>
        /// <param name="threadIndex">index</param>
        /// <returns>thread info</returns>
        /// <exception cref="DiagnosticsException">invalid thread index</exception>
        IThread GetThreadFromIndex(int threadIndex);

        /// <summary>
        /// Get the thread info from the OS thread id
        /// </summary>
        /// <param name="threadId">os id</param>
        /// <returns>thread info</returns>
        /// <exception cref="DiagnosticsException">invalid thread id</exception>
        IThread GetThreadFromId(uint threadId);
    }
}
