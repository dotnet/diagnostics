// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring
{
    internal sealed class ClientConnectionsSource : IDiagnosticsConnectionsSourceInternal
    {
        public Task<IEnumerable<IDiagnosticsConnection>> GetConnectionsAsync(CancellationToken token)
        {
            List<IDiagnosticsConnection> connections = new List<IDiagnosticsConnection>();
            foreach (int pid in DiagnosticsClient.GetPublishedProcesses())
            {
                // CONSIDER: Generate a "runtime instance identifier" based on the pipe name
                // e.g. pid + disambiguator in GUID form.
                connections.Add(new DiagnosticsConnection(pid));
            }

            return Task.FromResult(connections.AsReadOnly().AsEnumerable());
        }

        private class DiagnosticsConnection : IDiagnosticsConnection
        {
            public DiagnosticsConnection(int processId)
            {
                Endpoint = new PidIpcEndpoint(processId);
                ProcessId = processId;
            }

            public IIpcEndpoint Endpoint { get; }

            public int ProcessId { get; }

            public Guid RuntimeInstanceCookie => Guid.Empty;
        }
    }
}
