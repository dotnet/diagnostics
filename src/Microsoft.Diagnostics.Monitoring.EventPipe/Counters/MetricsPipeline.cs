// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class MetricsPipeline : EventSourcePipeline<MetricsPipelineSettings>
    {
        private readonly IEnumerable<ICountersLogger> _loggers;
        private readonly CounterFilter _filter;
        private string _sessionId;

        public MetricsPipeline(DiagnosticsClient client,
            MetricsPipelineSettings settings,
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
            var config = new MetricSourceConfiguration(Settings.CounterIntervalSeconds, Settings.CounterGroups.Select((EventPipeCounterGroup counterGroup) => new MetricEventPipeProvider
                {
                    Provider = counterGroup.ProviderName,
                    IntervalSeconds = counterGroup.IntervalSeconds,
                    Type = (MetricType)counterGroup.Type
                }),
                Settings.MaxHistograms, Settings.MaxTimeSeries);

            _sessionId = config.SessionId;

            return config;
        }

        protected override async Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            await ExecuteCounterLoggerActionAsync((metricLogger) => metricLogger.PipelineStarted(token));

            eventSource.Dynamic.All += traceEvent =>
            {
                try
                {
                    if (traceEvent.TryGetCounterPayload(_filter, _sessionId, out ICounterPayload counterPayload))
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

            await ExecuteCounterLoggerActionAsync((metricLogger) => metricLogger.PipelineStopped(token));
        }

        private async Task ExecuteCounterLoggerActionAsync(Func<ICountersLogger, Task> action)
        {
            foreach (ICountersLogger logger in _loggers)
            {
                try
                {
                    await action(logger);
                }
                catch (ObjectDisposedException)
                {
                }
            }
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
