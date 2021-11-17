// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
                _filter = new CounterFilter(Settings.CounterIntervalSeconds);
                foreach (var counterGroup in settings.CounterGroups)
                {
                    _filter.AddFilter(counterGroup.ProviderName, counterGroup.CounterNames);
                }
            }
            else
            {
                _filter = CounterFilter.AllCounters(Settings.CounterIntervalSeconds);
            }
        }

        protected override MonitoringSourceConfiguration CreateConfiguration()
        {
            return new MetricSourceConfiguration(Settings.CounterIntervalSeconds, _filter.GetProviders());
        }

        protected override async Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            ExecuteCounterLoggerAction((metricLogger) => metricLogger.PipelineStarted());

            eventSource.Dynamic.All += traceEvent =>
            {
                try
                {
                    if (traceEvent.TryGetCounterPayload(_filter, out ICounterPayload counterPayload))
                    {
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
    }
}
