// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventCounterPipeline : EventSourcePipeline<EventPipeCounterPipelineSettings>
    {
        private readonly IEnumerable<ICountersLogger> _metricsLogger;
        private readonly CounterFilter _filter;

        public EventCounterPipeline(DiagnosticsClient client,
            EventPipeCounterPipelineSettings settings,
            IEnumerable<ICountersLogger> metricsLogger) : base(client, settings)
        {
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));

            if (settings.CounterGroups.Length > 0)
            {
                _filter = new CounterFilter();
                foreach (var counterGroup in settings.CounterGroups)
                {
                    _filter.AddFilter(counterGroup.ProviderName, counterGroup.CounterNames);
                }
            }
            else
            {
                _filter = CounterFilter.AllCounters;
            }
        }

        internal override DiagnosticsEventPipeProcessor CreateProcessor()
        {
            return new DiagnosticsEventPipeProcessor(PipeMode.Metrics, metricLoggers: _metricsLogger, metricIntervalSeconds: (int)Settings.RefreshInterval.TotalSeconds,
                metricFilter: _filter);
        }
    }
}
