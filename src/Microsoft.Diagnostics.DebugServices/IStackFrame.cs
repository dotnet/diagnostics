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
        ulong ModuleBase { get; }

        /// <summary>
        /// Returns the module, method name and displacement
        /// </summary>
        /// <param name="moduleName">the module name of the method or null</param>
        /// <param name="methodName">the method name or null</param>
        /// <param name="displacement">the offset from the beginning of the function</param>
        void GetMethodName(out string moduleName, out string methodName, out ulong displacement);
    }
}
