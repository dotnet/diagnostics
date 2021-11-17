// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// This interface represents the dump, snapshot or live session memory, threads and modules, etc.
    /// </summary>
    public interface ITarget
    {
        /// <summary>
        /// Returns the host interface instance
        /// </summary>
        IHost Host { get; }

        /// <summary>
        /// The target id
        /// </summary>
        int Id { get; }

        /// <summary>
        /// Returns the target OS (which may be different from the OS this is running on)
        /// </summary>
        OSPlatform OperatingSystem { get; }

        /// <summary>
        /// The target architecture/processor
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">unsupported architecture</exception>
        Architecture Architecture { get; }

        /// <summary>
        /// Returns true if dump, false if live session or snapshot
        /// </summary>
        bool IsDump { get; }

        /// <summary>
        /// The target's process id or null if no process
        /// </summary>
        uint? ProcessId { get; }

        /// <summary>
        /// Returns the unique temporary directory for this instance of SOS
        /// </summary>
        string GetTempDirectory();

        /// <summary>
        /// The per target services.
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Invoked when this target is flushed (via the Flush() call).
        /// </summary>
        IServiceEvent OnFlushEvent { get; }

        /// <summary>
        /// Flushes any cached state in the target.
        /// </summary>
        void Flush();

        /// <summary>
        /// Invoked when the target is destroyed.
        /// </summary>
        IServiceEvent OnDestroyEvent { get; }
    }
}
