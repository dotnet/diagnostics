// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.EventPipe;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    internal sealed class MetricsLogger : ICountersLogger
    {
        private readonly IMetricsStore _store;

        public MetricsLogger(IMetricsStore metricsStore)
        {
            _store = metricsStore;
        }

        public void Log(ICounterPayload metric)
        {
            _store.AddMetric(metric);
        }

        public void PipelineStarted()
        {
        }

        public void PipelineStopped()
        {
        }
    }
}
