// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring
{
    internal interface IDiagnosticsConnection
    {
        IIpcEndpoint Endpoint { get; }

        int ProcessId { get; }

        Guid RuntimeInstanceCookie { get; }
    }

    public interface IDiagnosticsConnectionsSource
    {
    }

    internal interface IDiagnosticsConnectionsSourceInternal : IDiagnosticsConnectionsSource
    {
        Task<IEnumerable<IDiagnosticsConnection>> GetConnectionsAsync(CancellationToken token);
    }
}
