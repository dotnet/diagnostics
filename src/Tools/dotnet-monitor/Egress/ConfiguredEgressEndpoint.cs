// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.Egress;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal abstract class ConfiguredEgressEndpoint
    {
        public abstract Task<EgressResult> EgressAsync(
            Func<CancellationToken, Task<Stream>> action,
            string fileName,
            string contentType,
            IEndpointInfo source,
            CancellationToken token);

        public abstract Task<EgressResult> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string fileName,
            string contentType,
            IEndpointInfo source,
            CancellationToken token);
    }
}
