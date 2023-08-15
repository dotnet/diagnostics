// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.SystemDiagnosticsMetrics
{
    /// <summary>
    /// Trigger that detects when the specified instrument's value is held
    /// above, below, or between threshold values for a specified duration of time.
    /// </summary>
    internal sealed class SystemDiagnosticsMetricsTrigger :
        ITraceEventTrigger
    {
        // A cache of the list of events that are expected from the specified event provider.
        // This is a mapping of event provider name to the event map returned by GetProviderEventMap.
        // This allows caching of the event map between multiple instances of the trigger that
        // use the same event provider as the source of counter events.
        private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, IReadOnlyCollection<string>>> _eventMapCache =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly CounterFilter _filter;
        private readonly SystemDiagnosticsMetricsTriggerImpl _impl;
        private readonly string _meterName;
        private readonly string _clientId;
        private readonly string _sessionId;

        public SystemDiagnosticsMetricsTrigger(SystemDiagnosticsMetricsTriggerSettings settings)
        {
            if (null == settings)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Validate(settings);

            _filter = new CounterFilter(settings.CounterIntervalSeconds);
            _filter.AddFilter(settings.MeterName, new string[] { settings.InstrumentName });

            _impl = new SystemDiagnosticsMetricsTriggerImpl(settings);

            _meterName = settings.MeterName;

            _sessionId = settings.SessionId;

            _clientId = settings.ClientId;
        }

        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetProviderEventMap()
        {
            return _eventMapCache.GetOrAdd(_meterName, CreateEventMapForProvider);
        }

        public bool HasSatisfiedCondition(TraceEvent traceEvent)
        {
            // Filter to the counter of interest before forwarding to the implementation
            if (traceEvent.TryGetCounterPayload(new CounterConfiguration(_filter) { SessionId = _sessionId, ClientId = _clientId }, out ICounterPayload payload))
            {
                return _impl.HasSatisfiedCondition(payload);
            }
            return false;
        }

        public static MetricSourceConfiguration CreateConfiguration(SystemDiagnosticsMetricsTriggerSettings settings)
        {
            Validate(settings);

            MetricSourceConfiguration config = new(
                settings.CounterIntervalSeconds,
                MetricSourceConfiguration.CreateProviders(new string[] { settings.MeterName }, MetricType.Meter),
                settings.MaxHistograms,
                settings.MaxTimeSeries,
                useSharedSession: settings.UseSharedSession);
            settings.ClientId = config.ClientId;
            settings.SessionId = config.SessionId;

            return config;
        }

        private static void Validate(SystemDiagnosticsMetricsTriggerSettings settings)
        {
            ValidationContext context = new(settings);
            Validator.ValidateObject(settings, context, validateAllProperties: true);
        }

        private IReadOnlyDictionary<string, IReadOnlyCollection<string>> CreateEventMapForProvider(string providerName)
        {
            return new ReadOnlyDictionary<string, IReadOnlyCollection<string>>(
                new Dictionary<string, IReadOnlyCollection<string>>()
                {
                    { "System.Diagnostics.Metrics", new ReadOnlyCollection<string>(Array.Empty<string>()) }
                });
        }
    }
}
