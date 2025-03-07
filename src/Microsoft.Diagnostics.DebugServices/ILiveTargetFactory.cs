// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Attaches to live processes.
    /// </summary>
    public interface ILiveTargetFactory
    {
        /// <summary>
        /// Attaches to a live process and suspends it until the target is destroyed/closed.
        /// </summary>
        /// <returns>target instance</returns>
        /// <exception cref="DiagnosticsException">can not construct target instance</exception>
        ITarget Attach(int processId);
    }
}
