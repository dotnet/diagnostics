// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal class EgressService : IEgressService
    {
        private readonly IOptionsMonitor<EgressOptions> _egressOptions;

        public EgressService(IOptionsMonitor<EgressOptions> egressOptions)
        {
            _egressOptions = egressOptions;
        }

        public Task<EgressResult> EgressAsync(string endpointName, Func<CancellationToken, Task<Stream>> action, string fileName, string contentType, IEndpointInfo source, CancellationToken token)
        {
            if (_egressOptions.CurrentValue.Endpoints.TryGetValue(endpointName, out ConfiguredEgressEndpoint endpoint))
            {
                return endpoint.EgressAsync(action, fileName, contentType, source, token);
            }
            throw new InvalidOperationException(FormattableString.Invariant($"Egress endpoint '{endpointName}' does not exist."));
        }

        public Task<EgressResult> EgressAsync(string endpointName, Func<Stream, CancellationToken, Task> action, string fileName, string contentType, IEndpointInfo source, CancellationToken token)
        {
            if (_egressOptions.CurrentValue.Endpoints.TryGetValue(endpointName, out ConfiguredEgressEndpoint endpoint))
            {
                return endpoint.EgressAsync(action, fileName, contentType, source, token);
            }
            throw new InvalidOperationException(FormattableString.Invariant($"Egress endpoint '{endpointName}' does not exist."));
        }
    }
}
