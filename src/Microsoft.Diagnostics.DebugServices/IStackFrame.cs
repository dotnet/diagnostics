// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Describes a stack frame
    /// </summary>
    public interface IStackFrame
    {
        /// <summary>
        /// The instruction pointer for this frame
        /// </summary>
        ulong InstructionPointer { get; }

        /// <summary>
        /// The stack pointer of this frame or 0
        /// </summary>
        ulong StackPointer { get; }

        /// <summary>
        /// The module base of the IP
        /// </summary>
        public ulong ModuleBase { get; }

        /// <summary>
        /// Offset from beginning of method
        /// </summary>
        uint Offset { get; }

        /// <summary>
        /// The exception type name
        /// </summary>
        string MethodName { get; }
    }
}
