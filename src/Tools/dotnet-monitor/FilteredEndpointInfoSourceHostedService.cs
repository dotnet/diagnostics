// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    /// <summary>
    /// A hosted service that ensures the <see cref="FilteredEndpointInfoSource"/>
    /// starts monitoring for connectable processes.
    /// </summary>
    internal class FilteredEndpointInfoSourceHostedService : IHostedService
    {
        private readonly FilteredEndpointInfoSource _source;

        public FilteredEndpointInfoSourceHostedService(IEndpointInfoSource source)
        {
            _source = (FilteredEndpointInfoSource)source;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _source.Start();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
