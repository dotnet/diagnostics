// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// The kind or reason of crash for the triage JSON
    /// </summary>
    public enum CrashReason
    {
        Unknown = 0,
        UnhandledException = 1,
        EnvironmentFailFast = 2,
        InternalFailFast = 3,
    }

    /// <summary>
    /// Crash information service. Details about the unhandled exception or crash.
    /// </summary>
    public interface ICrashInfoService
    {
        /// <summary>
        /// The kind or reason for the crash
        /// </summary>
        CrashReason CrashReason { get; }

        /// <summary>
        /// Crashing OS thread id
        /// </summary>
        uint ThreadId { get; }

        /// <summary>
        /// The HRESULT passed to Watson
        /// </summary>
        uint HResult { get; }

        /// <summary>
        /// Runtime type or flavor
        /// </summary>
        RuntimeType RuntimeType { get; }

        /// <summary>
        /// Runtime version and possible commit id
        /// </summary>
        string RuntimeVersion { get; }

        /// <summary>
        /// Crash or FailFast message
        /// </summary>
        string Message { get; }

        /// <summary>
        /// The exception that caused the crash or null
        /// </summary>
        IManagedException Exception { get; }
    }
}
