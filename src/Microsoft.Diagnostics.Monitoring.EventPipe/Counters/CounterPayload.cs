// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class CounterPayload : ICounterPayload
    {
#if NETSTANDARD
        private static readonly IReadOnlyDictionary<string, string> Empty = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0));
#else
        private static readonly IReadOnlyDictionary<string, string> Empty = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty;
#endif

        public CounterPayload(DateTime timestamp,
            string provider,
            string name,
            string displayName,
            string unit,
            double value,
            CounterType counterType,
            float interval,
            Dictionary<string, string> metadata)
        {
            Timestamp = timestamp;
            Name = name;
            DisplayName = displayName;
            Unit = unit;
            Value = value;
            CounterType = counterType;
            Provider = provider;
            Interval = interval;
            Metadata = metadata ?? Empty;
            EventType = EventType.Gauge;
        }

        // Copied from dotnet-counters
        public CounterPayload(string providerName, string name, string displayName, string displayUnits, Dictionary<string, string> metadata, double value, DateTime timestamp, string type, EventType eventType)
        {
            Provider = providerName;
            Name = name;
            Metadata = metadata ?? Empty;
            Value = value;
            Timestamp = timestamp;
            CounterType = (CounterType)Enum.Parse(typeof(CounterType), type);
            EventType = eventType;
        }

        public string Namespace { get; }

        public string Name { get; }

        public string DisplayName { get; protected set; }

        public string Unit { get; }

        public double Value { get; }

        public DateTime Timestamp { get; }

        public float Interval { get; }

        public CounterType CounterType { get; }

        public string Provider { get; }

        public string Tags { get; private set; }

        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(0);

        public EventType EventType { get; set; }

    }

    class GaugePayload : CounterPayload
    {
        public GaugePayload(string providerName, string name, string displayName, string displayUnits, Dictionary<string, string> metadata, double value, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, metadata, value, timestamp, "Metric", EventType.Gauge)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }
    }

    class RatePayload : CounterPayload
    {
        public RatePayload(string providerName, string name, string displayName, string displayUnits, Dictionary<string, string> metadata, double value, double intervalSecs, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, metadata, value, timestamp, "Rate", EventType.Rate)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            string unitsName = string.IsNullOrEmpty(displayUnits) ? "Count" : displayUnits;
            string intervalName = intervalSecs.ToString() + " sec";
            DisplayName = $"{counterName} ({unitsName} / {intervalName})";
        }
    }

    class PercentilePayload : CounterPayload
    {
        public PercentilePayload(string providerName, string name, string displayName, string displayUnits, Dictionary<string, string> metadata, double val, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, metadata, val, timestamp, "Metric", EventType.Histogram)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }
    }

    class ErrorPayload : CounterPayload
    {
        public ErrorPayload(string providerName, string name, string displayName, string displayUnits, Dictionary<string, string> metadata, double val, DateTime timestamp, string errorMessage) :
            base(providerName, name, displayName, displayUnits, metadata, val, timestamp, "Metric", EventType.Error)
        {
            ErrorMessage = errorMessage;
        }

        public string ErrorMessage { get; private set; }
    }

    // If keep this, should probably put it somewhere else
    enum EventType : int
    {
        Rate,
        Gauge,
        Histogram,
        Error
    }
}