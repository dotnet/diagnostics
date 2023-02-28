// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Console output service
    /// </summary>
    public interface IContextService
    {
        /// <summary>
        /// Current context service provider. Contains the current ITarget, IThread
        /// and IRuntime instances along with all per target and global services.
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Fires anytime the current context changes.
        /// </summary>
        IServiceEvent OnContextChange { get; }

        /// <summary>
        /// Sets the current target.
        /// </summary>
        /// <param name="targetId">target id</param>
        void SetCurrentTarget(int targetId);

        /// <summary>
        /// Clears (nulls) the current target
        /// </summary>
        void ClearCurrentTarget();

        /// <summary>
        /// Set the current thread.
        /// </summary>
        /// <param name="threadId">thread id</param>
        void SetCurrentThread(uint threadId);

        /// <summary>
        /// Clears (nulls) the current thread.
        /// </summary>
        void ClearCurrentThread();

        /// <summary>
        /// Set the current runtime
        /// </summary>
        /// <param name="runtimeId">runtime id</param>
        void SetCurrentRuntime(int runtimeId);

        /// <summary>
        /// Clears (nulls) the current runtime
        /// </summary>
        void ClearCurrentRuntime();
    }
}
