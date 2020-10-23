// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal sealed class MetricsStoreService : IDisposable
    {
        public MetricsStore MetricsStore { get; }
        
        public MetricsStoreService(
            IOptions<PrometheusConfiguration> options)
        {
            MetricsStore = new MetricsStore(options.Value.MetricCount);
        }

        public void Dispose()
        {
            MetricsStore.Dispose();
        }
    }
}
