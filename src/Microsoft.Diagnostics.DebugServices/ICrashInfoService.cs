// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

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
        /// The module base address that contains the runtime
        /// </summary>
        ulong RuntimeBaseAddress { get; }

        /// <summary>
        /// Runtime version and possible commit id
        /// </summary>
        string RuntimeVersion { get; }

        /// <summary>
        /// Crash or FailFast message
        /// </summary>
        string Message { get; }

        /// <summary>
        /// The exception at the address
        /// </summary>
        /// <param name="address">address of exception object or 0 for current exception</param>
        /// <returns>exception or null if none</returns>
        /// <exception cref="ArgumentOutOfRangeException">invalid exception address</exception>
        IException GetException(ulong address);

        /// <summary>
        /// Returns the thread's current exception.
        /// </summary>
        /// <param name="threadId">OS thread id</param>
        /// <returns>exception or null if none</returns>
        /// <exception cref="ArgumentOutOfRangeException">invalid thread id</exception>
        IException GetThreadException(uint threadId);

        /// <summary>
        /// Returns all the thread's nested exception.
        /// </summary>
        /// <param name="threadId">OS thread id</param>
        /// <returns>array of nested exceptions</returns>
        /// <exception cref="ArgumentOutOfRangeException">invalid thread id</exception>
        IEnumerable<IException> GetNestedExceptions(uint threadId);
    }
}
