// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Details about a thread
    /// </summary>
    public interface IThread
    {
        /// <summary>
        /// Debugger specific thread index.
        /// </summary>
        int ThreadIndex { get; }

        /// <summary>
        /// OS thread id.
        /// </summary>
        uint ThreadId { get; }

        /// <summary>
        /// The target for this target.
        /// </summary>
        ITarget Target { get; }

        /// <summary>
        /// The per thread services.
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Returns the register value for the thread and register index. This function
        /// can only return register values that are 64 bits or less and currently the
        /// clrmd data targets don't return any floating point or larger registers.
        /// </summary>
        /// <param name="registerIndex">register index</param>
        /// <param name="value">value returned</param>
        /// <returns>true if value found</returns>
        bool TryGetRegisterValue(int registerIndex, out ulong value);

        /// <summary>
        /// Returns the raw context buffer bytes for the specified thread.
        /// </summary>
        /// <returns>register context</returns>
        /// <exception cref="DiagnosticsException">invalid thread</exception>
        byte[] GetThreadContext();

        /// <summary>
        /// Returns the address of the Windows TEB or 0.
        /// </summary>
        /// <returns>TEB address</returns>
        public ulong GetThreadTeb();
    }
}
