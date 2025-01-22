// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// The type of the debugger or host. Must match IHost::HostType.
    /// </summary>
    public enum HostType
    {
        DotnetDump,
        Lldb,
        DbgEng,
        Vs
    };

    /// <summary>
    /// Host interface
    /// </summary>
    public interface IHost
    {
        /// <summary>
        /// Fires on hosting debugger or dotnet-dump shutdown
        /// </summary>
        IServiceEvent OnShutdownEvent { get; }

        /// <summary>
        /// Fires when an new target is created.
        /// </summary>
        IServiceEvent<ITarget> OnTargetCreate { get; }

        /// <summary>
        /// Returns the hosting debugger type
        /// </summary>
        HostType HostType { get; }

        /// <summary>
        /// Global service provider
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Enumerates all the targets
        /// </summary>
        IEnumerable<ITarget> EnumerateTargets();

        /// <summary>
        /// Add this target to the host. It is left up to the target
        /// implementation to fire the OnTargetCreate event when the
        /// it is completely instaniated.
        /// </summary>
        /// <param name="target">target instance</param>
        /// <returns>target id</returns>
        int AddTarget(ITarget target);

        /// <summary>
        /// Returns the unique temporary directory for this debug session
        /// </summary>
        string GetTempDirectory();
    }
}
