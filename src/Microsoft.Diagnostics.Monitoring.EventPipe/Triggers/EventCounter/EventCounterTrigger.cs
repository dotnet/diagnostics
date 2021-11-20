// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.EventCounter
{
    /// <summary>
    /// Trigger that detects when the specified event source counter value is held
    /// above, below, or between threshold values for a specified duration of time.
    /// </summary>
    internal sealed class EventCounterTrigger :
        ITraceEventTrigger
    {
        // A cache of the list of events that are expected from the specified event provider.
        // This is a mapping of event provider name to the event map returned by GetProviderEventMap.
        // This allows caching of the event map between multiple instances of the trigger that
        // use the same event provider as the source of counter events.
        private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, IReadOnlyCollection<string>>> _eventMapCache =
            new ConcurrentDictionary<string, IReadOnlyDictionary<string, IReadOnlyCollection<string>>>(StringComparer.OrdinalIgnoreCase);

        // Only care for the EventCounters events from any of the specified providers, thus
        // create a static readonly instance that is shared among all event maps.
        private static readonly IReadOnlyCollection<string> _eventProviderEvents =
            new ReadOnlyCollection<string>(new string[] { "EventCounters" });

        private readonly CounterFilter _filter;
        private readonly EventCounterTriggerImpl _impl;
        private readonly string _providerName;

        public EventCounterTrigger(EventCounterTriggerSettings settings)
        {
            if (null == settings)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Validate(settings);

            _filter = new CounterFilter(settings.CounterIntervalSeconds);
            _filter.AddFilter(settings.ProviderName, new string[] { settings.CounterName });

            _impl = new EventCounterTriggerImpl(settings);

            _providerName = settings.ProviderName;
        }

        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetProviderEventMap()
        {
            return _eventMapCache.GetOrAdd(_providerName, CreateEventMapForProvider);
        }

        public bool HasSatisfiedCondition(TraceEvent traceEvent)
        {
            // Filter to the counter of interest before forwarding to the implementation
            if (traceEvent.TryGetCounterPayload(_filter, out ICounterPayload payload))
            {
                return _impl.HasSatisfiedCondition(payload);
            }
            return false;
        }

        public static MonitoringSourceConfiguration CreateConfiguration(EventCounterTriggerSettings settings)
        {
            Validate(settings);

            return new MetricSourceConfiguration(settings.CounterIntervalSeconds, new string[] { settings.ProviderName });
        }

        private static void Validate(EventCounterTriggerSettings settings)
        {
            ValidationContext context = new(settings);
            Validator.ValidateObject(settings, context, validateAllProperties: true);
        }

        private IReadOnlyDictionary<string, IReadOnlyCollection<string>> CreateEventMapForProvider(string providerName)
        {
            return new ReadOnlyDictionary<string, IReadOnlyCollection<string>>(
                new Dictionary<string, IReadOnlyCollection<string>>()
                {
                    { _providerName, _eventProviderEvents }
                });
        }
    }
}
