// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.EventCounter
{
    /// <summary>
    /// Trigger that detects when the specified event source counter value is held
    /// above, below, or between threshold values for a specified duration of time.
    /// </summary>
    internal sealed class EventCounterTrigger :
        ITraceEventTrigger
    {
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
            
            _impl = new(settings);

            _providerName = settings.ProviderName;
        }

        public IDictionary<string, IEnumerable<string>> GetProviderEventMap()
        {
            return new Dictionary<string, IEnumerable<string>>()
            {
                { _providerName, new string[] { "EventCounters" } }
            };
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
    }
}
