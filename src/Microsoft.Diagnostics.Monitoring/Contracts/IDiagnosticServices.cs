// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    /// <summary>
    /// Set of services provided by the monitoring tool. These are consumed by
    /// the REST Api.
    /// </summary>
    public interface IDiagnosticServices : IDisposable
    {
        IEnumerable<int> GetProcesses();

        int ResolveProcess(int? pid);

        Task<Stream> GetDump(int pid, DumpType mode);

        Task<Stream> GetGcDump(int pid, CancellationToken token);

        Task<IStreamWithCleanup> StartTrace(int pid, MonitoringSourceConfiguration configuration, TimeSpan duration, CancellationToken token);

        Task StartLogs(Stream outputStream, int pid, TimeSpan duration, LogFormat logFormat, LogLevel logLevel, CancellationToken token);
    }

    public interface IStreamWithCleanup : IAsyncDisposable
    {
        Stream Stream { get; }
    }

    public enum DumpType
    {
        Full = 1,
        Mini,
        WithHeap,
        Triage
    }

    public enum LogFormat
    {
        None = 0,
        Json = 1,
        EventStream = 2
    }

    [Flags]
    public enum TraceProfile
    {
        Cpu =     0x1,
        Http =    0x2,
        Logs =    0x4,
        Metrics = 0x8
    }
}
