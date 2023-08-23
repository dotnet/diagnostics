// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrThreadPool
    {
        /// <summary>
        /// Whether this runtime is using the Portable threadpool or not.
        /// </summary>
        bool Portable { get; }

        /// <summary>
        /// The current CPU utilization of the ThreadPool (a number between 0 and 100).
        /// </summary>
        int CpuUtilization { get; }

        /// <summary>
        /// The minimum number of worker threads allowed for the ThreadPool.
        /// </summary>
        int MinThreads { get; }

        /// <summary>
        /// The maximum number of worker threads allowed for the ThreadPool.
        /// </summary>
        int MaxThreads { get; }

        /// <summary>
        /// The number of idle worker threads.
        /// </summary>
        int IdleWorkerThreads { get; }

        /// <summary>
        /// The number of active worker threads.
        /// </summary>
        int ActiveWorkerThreads { get; }

        int TotalCompletionPorts { get; }
        int FreeCompletionPorts { get; }
        int MaxFreeCompletionPorts { get; }
        int CompletionPortCurrentLimit { get; }
        int MinCompletionPorts { get; }
        int MaxCompletionPorts { get; }

        /// <summary>
        /// The number of retired worker threads.
        /// </summary>
        int RetiredWorkerThreads { get; }

        /// <summary>
        /// Enumerates LegacyThreadPoolWorkRequests.  We only have this for Desktop CLR.
        /// </summary>
        /// <returns>An enumeration of work requests, or an empty enumeration of the runtime
        /// does not have them.</returns>
        IEnumerable<LegacyThreadPoolWorkRequest> EnumerateLegacyWorkRequests();

        /// <summary>
        /// Enumerates the ThreadPool's HillClimbing log.  This is the log of why we decided to add
        /// or remove threads from the ThreadPool.
        /// Note this is currently only supported on .Net Core and not Desktop CLR.
        /// </summary>
        /// <returns>An enumeration of the HillClimbing log, or an empty enumeration for Desktop CLR.</returns>
        IEnumerable<HillClimbingLogEntry> EnumerateHillClimbingLog();
    }
}
