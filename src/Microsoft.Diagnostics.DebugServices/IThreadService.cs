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
        bool GetRegisterIndexByName(string name, out int registerIndex);

        /// <summary>
        /// Returns the register info (name, offset, size, etc).
        /// </summary>
        /// <param name="registerIndex">register index</param>
        /// <param name="info">RegisterInfo</param>
        /// <returns>true if index found</returns>
        bool GetRegisterInfo(int registerIndex, out RegisterInfo info);

        /// <summary>
        /// Returns the register value for the thread and register index. This function
        /// can only return register values that are 64 bits or less and currently the
        /// clrmd data targets don't return any floating point or larger registers.
        /// </summary>
        /// <param name="threadId">thread id</param>
        /// <param name="registerIndex">register index</param>
        /// <param name="value">value returned</param>
        /// <returns>true if value found</returns>
        bool GetRegisterValue(uint threadId, int registerIndex, out ulong value);

        /// <summary>
        /// Returns the raw context buffer bytes for the specified thread.
        /// </summary>
        /// <param name="threadId">thread id</param>
        /// <returns>register context</returns>
        /// <exception cref="DiagnosticsException">invalid thread id</exception>
        byte[] GetThreadContext(uint threadId);

        /// <summary>
        /// Enumerate all the native threads
        /// </summary>
        /// <returns>ThreadInfos for all the threads</returns>
        IEnumerable<ThreadInfo> EnumerateThreads();

        /// <summary>
        /// Get the thread info from the thread index
        /// </summary>
        /// <param name="threadIndex">index</param>
        /// <returns>thread info</returns>
        /// <exception cref="DiagnosticsException">invalid thread index</exception>
        ThreadInfo GetThreadInfoFromIndex(int threadIndex);

        /// <summary>
        /// Get the thread info from the OS thread id
        /// </summary>
        /// <param name="threadId">os id</param>
        /// <returns>thread info</returns>
        /// <exception cref="DiagnosticsException">invalid thread id</exception>
        ThreadInfo GetThreadInfoFromId(uint threadId);
    }
}
