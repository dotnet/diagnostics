﻿// Licensed to the .NET Foundation under one or more agreements.
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
    internal sealed class ClientEndpointInfoSource : IEndpointInfoSourceInternal
    {
        public Task<IEnumerable<IEndpointInfo>> GetEndpointInfoAsync(CancellationToken token)
        {
            List<IEndpointInfo> endpointInfos = new List<IEndpointInfo>();
            foreach (int pid in DiagnosticsClient.GetPublishedProcesses())
            {
                // CONSIDER: Generate a "runtime instance identifier" based on the pipe name
                // e.g. pid + disambiguator in GUID form.
                endpointInfos.Add(new EndpointInfo(pid));
            }

            return Task.FromResult(endpointInfos.AsEnumerable());
        }

        private class EndpointInfo : IEndpointInfo
        {
            public EndpointInfo(int processId)
            {
                Endpoint = new PidIpcEndpoint(processId);
                ProcessId = processId;
            }

            public IpcEndpoint Endpoint { get; }

            public int ProcessId { get; }

            public Guid RuntimeInstanceCookie => Guid.Empty;
        }
    }
}
