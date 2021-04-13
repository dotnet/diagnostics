// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventCounterPipeline : EventSourcePipeline<EventPipeCounterPipelineSettings>
    {
        private readonly IEnumerable<ICountersLogger> _loggers;
        private readonly CounterFilter _filter;

        public EventCounterPipeline(DiagnosticsClient client,
            EventPipeCounterPipelineSettings settings,
            IEnumerable<ICountersLogger> loggers) : base(client, settings)
        {
            _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));

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

        protected override MonitoringSourceConfiguration CreateConfiguration()
        {
            return new MetricSourceConfiguration(CounterIntervalSeconds, _filter.GetProviders());
        }

        protected override async Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            ExecuteCounterLoggerAction((metricLogger) => metricLogger.PipelineStarted());

            eventSource.Dynamic.All += traceEvent =>
            {
                try
                {
                    // Metrics
                    if (traceEvent.EventName.Equals("EventCounters"))
                    {
                        IDictionary<string, object> payloadVal = (IDictionary<string, object>)(traceEvent.PayloadValue(0));
                        IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                        //Make sure we are part of the requested series. If multiple clients request metrics, all of them get the metrics.
                        string series = payloadFields["Series"].ToString();
                        if (GetInterval(series) != CounterIntervalSeconds * 1000)
                        {
                            return;
                        }

                        string counterName = payloadFields["Name"].ToString();
                        if (!_filter.IsIncluded(traceEvent.ProviderName, counterName))
                        {
                            return;
                        }

                        float intervalSec = (float)payloadFields["IntervalSec"];
                        string displayName = payloadFields["DisplayName"].ToString();
                        string displayUnits = payloadFields["DisplayUnits"].ToString();
                        double value = 0;
                        CounterType counterType = CounterType.Metric;

                        if (payloadFields["CounterType"].Equals("Mean"))
                        {
                            value = (double)payloadFields["Mean"];
                        }
                        else if (payloadFields["CounterType"].Equals("Sum"))
                        {
                            counterType = CounterType.Rate;
                            value = (double)payloadFields["Increment"];
                            if (string.IsNullOrEmpty(displayUnits))
                            {
                                displayUnits = "count";
                            }
                            //TODO Should we make these /sec like the dotnet-counters tool?
                        }

                        // Note that dimensional data such as pod and namespace are automatically added in prometheus and azure monitor scenarios.
                        // We no longer added it here.
                        var counterPayload = new CounterPayload(traceEvent.TimeStamp,
                            traceEvent.ProviderName,
                            counterName, displayName,
                            displayUnits,
                            value,
                            counterType,
                            intervalSec);

                        ExecuteCounterLoggerAction((metricLogger) => metricLogger.Log(counterPayload));
                    }
                }
                catch (Exception)
                {
                }
            };

            using var sourceCompletedTaskSource = new EventTaskSource<Action>(
                taskComplete => taskComplete,
                handler => eventSource.Completed += handler,
                handler => eventSource.Completed -= handler,
                token);

            await sourceCompletedTaskSource.Task;

            ExecuteCounterLoggerAction((metricLogger) => metricLogger.PipelineStopped());
        }

        private static int GetInterval(string series)
        {
            const string comparison = "Interval=";
            int interval = 0;
            if (series.StartsWith(comparison, StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(series.Substring(comparison.Length), out interval);
            }
            return interval;
        }

        private void ExecuteCounterLoggerAction(Action<ICountersLogger> action)
        {
            foreach (ICountersLogger logger in _loggers)
            {
                try
                {
                    action(logger);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private int CounterIntervalSeconds => (int)Settings.RefreshInterval.TotalSeconds;
    }
}
