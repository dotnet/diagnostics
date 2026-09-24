// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Provides native stack frames reported by the debugger.
    /// </summary>
    public interface INativeThreadStackService
    {
        /// <summary>
        /// Gets the native stack frames for the specified operating system thread.
        /// </summary>
        /// <param name="threadId">Operating system thread identifier.</param>
        /// <param name="maxFrames">Maximum number of frames to return.</param>
        /// <returns>The native stack frames in unwind order.</returns>
        IReadOnlyList<NativeStackFrame> GetStackTrace(uint threadId, int maxFrames);
    }

    /// <summary>
    /// A native stack frame reported by the debugger.
    /// </summary>
    public readonly struct NativeStackFrame
    {
        public NativeStackFrame(ulong instructionPointer, ulong? stackPointer)
        {
            InstructionPointer = instructionPointer;
            StackPointer = stackPointer;
        }

        /// <summary>
        /// The instruction pointer for the frame.
        /// </summary>
        public ulong InstructionPointer { get; }

        /// <summary>
        /// The stack pointer for the frame, or null when the debugger cannot provide one.
        /// </summary>
        public ulong? StackPointer { get; }
    }
}
