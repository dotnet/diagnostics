// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class MetricsPipeline : EventSourcePipeline<MetricsPipelineSettings>
    {
        private readonly IEnumerable<ICountersLogger> _loggers;
        private readonly CounterFilter _filter;
        private string _clientId;

        public MetricsPipeline(DiagnosticsClient client,
            MetricsPipelineSettings settings,
            IEnumerable<ICountersLogger> loggers) : base(client, settings)
        {
            _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));

            if (settings.CounterGroups.Length > 0)
            {
                _filter = new CounterFilter(Settings.CounterIntervalSeconds);
                foreach (EventPipeCounterGroup counterGroup in settings.CounterGroups)
                {
                    _filter.AddFilter(counterGroup.ProviderName, counterGroup.CounterNames, counterGroup.IntervalSeconds);
                }
            }
            else
            {
                _filter = CounterFilter.AllCounters(Settings.CounterIntervalSeconds);
            }
        }

        // Don't copy this all over the place
        private static bool TryParseVersion(string versionString, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(versionString))
            {
                return false;
            }

            // The version is of the SemVer2 form: <major>.<minor>.<patch>[-<prerelease>][+<metadata>]
            // Remove the prerelease and metadata version information before parsing.

            ReadOnlySpan<char> versionSpan = versionString;
            int metadataIndex = versionSpan.IndexOf('+');
            if (-1 == metadataIndex)
            {
                metadataIndex = versionSpan.Length;
            }

            ReadOnlySpan<char> noMetadataVersion = versionSpan[..metadataIndex];
            int prereleaseIndex = noMetadataVersion.IndexOf('-');
            if (-1 == prereleaseIndex)
            {
                prereleaseIndex = metadataIndex;
            }

            return Version.TryParse(noMetadataVersion[..prereleaseIndex], out version);
        }

        protected override MonitoringSourceConfiguration CreateConfiguration()
        {
            bool useSharedSession = false;
            if (TryParseVersion(Client.GetProcessInfo().ClrProductVersionString, out Version v))
            {
                if (v.Major >= 8)
                {
                    useSharedSession = true;
                }
            }

            MetricSourceConfiguration config = new(Settings.CounterIntervalSeconds, Settings.CounterGroups.Select((EventPipeCounterGroup counterGroup) => new MetricEventPipeProvider
            {
                Provider = counterGroup.ProviderName,
                IntervalSeconds = counterGroup.IntervalSeconds,
                Type = (MetricType)counterGroup.Type
            }),
                Settings.MaxHistograms, Settings.MaxTimeSeries);

            _clientId = config.ClientId;

            return config;
        }

        protected override async Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            await ExecuteCounterLoggerActionAsync((metricLogger) => metricLogger.PipelineStarted(token)).ConfigureAwait(false);

            eventSource.Dynamic.All += traceEvent => {
                try
                {
                    if (traceEvent.TryGetCounterPayload(_filter, _clientId, out ICounterPayload counterPayload))
                    {
                        ExecuteCounterLoggerAction((metricLogger) => metricLogger.Log(counterPayload));
                    }
                }
                catch (Exception)
                {
                }
            };

            using EventTaskSource<Action> sourceCompletedTaskSource = new(
                taskComplete => taskComplete,
                handler => eventSource.Completed += handler,
                handler => eventSource.Completed -= handler,
                token);

            await sourceCompletedTaskSource.Task.ConfigureAwait(false);

            await ExecuteCounterLoggerActionAsync((metricLogger) => metricLogger.PipelineStopped(token)).ConfigureAwait(false);
        }

        private async Task ExecuteCounterLoggerActionAsync(Func<ICountersLogger, Task> action)
        {
            foreach (ICountersLogger logger in _loggers)
            {
                try
                {
                    await action(logger).ConfigureAwait(false);
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
