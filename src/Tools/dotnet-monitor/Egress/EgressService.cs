﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Diagnostics.Tools.Monitor.Egress.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress
{
    /// <summary>
    /// Egress service implementation required by the REST server.
    /// </summary>
    internal class EgressService : IEgressService
    {
        private readonly IOptionsMonitor<EgressOptions> _egressOptions;

        public EgressService(IOptionsMonitor<EgressOptions> egressOptions)
        {
            _egressOptions = egressOptions;
        }

        public Task<EgressResult> EgressAsync(string providerName, Func<CancellationToken, Task<Stream>> action, string fileName, string contentType, IEndpointInfo source, CancellationToken token)
        {
            if (_egressOptions.CurrentValue.Providers.TryGetValue(providerName, out ConfiguredEgressProvider provider))
            {
                return provider.EgressAsync(action, fileName, contentType, source, token);
            }
            throw new EgressException($"Egress provider '{providerName}' does not exist.");
        }

        public Task<EgressResult> EgressAsync(string providerName, Func<Stream, CancellationToken, Task> action, string fileName, string contentType, IEndpointInfo source, CancellationToken token)
        {
            if (_egressOptions.CurrentValue.Providers.TryGetValue(providerName, out ConfiguredEgressProvider provider))
            {
                return provider.EgressAsync(action, fileName, contentType, source, token);
            }
            throw new EgressException($"Egress provider '{providerName}' does not exist.");
        }
    }
}
