// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    /// <summary>
    /// Set of services provided by the monitoring tool. These are consumed by
    /// the REST Api.
    /// </summary>
    public interface IDiagnosticServices : IDisposable
    {
        Task<IEnumerable<IProcessInfo>> GetProcessesAsync(CancellationToken token);

        Task<IProcessInfo> GetProcessAsync(ProcessFilter? filter, CancellationToken token);

        Task<Stream> GetDump(IProcessInfo pi, DumpType mode, CancellationToken token);
    }


    public interface IProcessInfo
    {
        DiagnosticsClient Client { get; }

        string CommandLine { get; }

        public string OperatingSystem { get; }

        public string ProcessArchitecture { get; }

        string ProcessName { get; }

        int ProcessId { get; }

        Guid RuntimeInstanceCookie { get; }
    }

    public enum DumpType
    {
        Full = 1,
        Mini,
        WithHeap,
        Triage
    }
}
