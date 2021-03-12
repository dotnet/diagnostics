﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
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
        /// Invoked on hosting debugger or dotnet-dump shutdown
        /// </summary>
        IServiceEvent OnShutdownEvent { get; }

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
        /// Current target instances or null
        /// </summary>
        ITarget CurrentTarget { get; }

        /// <summary>
        /// Sets the current target.
        /// </summary>
        /// <param name="targetid">target id</param>
        void SetCurrentTarget(int targetid);
    }
}
