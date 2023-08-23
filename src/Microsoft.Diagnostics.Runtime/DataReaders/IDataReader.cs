// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// An interface for reading data out of the target process.
    /// </summary>
    public interface IDataReader : IMemoryReader
    {
        /// <summary>
        /// The name of the target.  This should be a meaningful moniker such as the pid of the target
        /// process or the path to the dump being read.  This is primarily used when debugging to see
        /// what DataTarget is inspecting.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Gets a value indicating whether this data reader is safe to use in parallel from multiple threads.
        /// </summary>
        bool IsThreadSafe { get; }

        /// <summary>
        /// The platform that the target process was running on.
        /// </summary>
        OSPlatform TargetPlatform { get; }

        /// <summary>
        /// Gets the architecture of the target.
        /// </summary>
        /// <returns>The architecture of the target.</returns>
        Architecture Architecture { get; }

        /// <summary>
        /// Gets the process ID of the DataTarget.
        /// </summary>
        int ProcessId { get; }

        /// <summary>
        /// Enumerates modules in the target process.
        /// </summary>
        /// <returns>An enumerable of the modules in the target process.</returns>
        IEnumerable<ModuleInfo> EnumerateModules();

        /// <summary>
        /// Gets the thread context for the given thread.
        /// </summary>
        /// <param name="threadID">The OS thread ID to read the context from.</param>
        /// <param name="contextFlags">The requested context flags, or 0 for default flags.</param>
        /// <param name="context">A span to write the context to.</param>
        bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context);

        /// <summary>
        /// Informs the data reader that the user has requested all data be flushed.
        /// </summary>
        void FlushCachedData();
    }
}
