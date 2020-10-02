// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring
{
    internal sealed class ClientEndpointInfoSource : IEndpointInfoSourceInternal
    {
        public async Task<IEnumerable<IEndpointInfo>> GetEndpointInfoAsync(CancellationToken token)
        {
            var endpointInfoTasks = new List<Task<EndpointInfo>>();
            // Run the EndpointInfo creation parallel. The call to FromProcessId sends
            // a GetProcessInfo command to the runtime instance to get additional information.
            foreach (int pid in DiagnosticsClient.GetPublishedProcesses())
            {
                endpointInfoTasks.Add(Task.Run(() => EndpointInfo.FromProcessId(pid)));
            }

            await Task.WhenAll(endpointInfoTasks);

            return endpointInfoTasks.Select(t => t.Result);
        }
    }
}
