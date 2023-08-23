// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Defines the state of the thread from the runtime's perspective.
    /// </summary>
    public enum GCMode
    {
        /// <summary>
        /// In Cooperative mode the thread must cooperate before a GC may proceed.  This means when a GC
        /// starts, the runtime will attempt to suspend the thread at a safepoint but cannot immediately
        /// stop the thread until it synchronizes.
        /// </summary>
        Cooperative,

        /// <summary>
        /// In Preemptive mode the runtime is free to suspend the thread at any time for a GC to occur.
        /// </summary>
        Preemptive
    }
}