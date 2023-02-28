// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    /// <summary>
    /// TODO This is currently a duplication of the src\Tools\dotnet-counters\CounterPayload.cs stack. The two will be unified in a separate change.
    /// </summary>
    internal class CounterPayload : ICounterPayload
    {
        public CounterPayload(DateTime timestamp,
            string provider,
            string name,
            string displayName,
            string unit,
            double value,
            CounterType counterType,
            float interval,
            string metadata)
        {
            Timestamp = timestamp;
            Name = name;
            DisplayName = displayName;
            Unit = unit;
            Value = value;
            CounterType = counterType;
            Provider = provider;
            Interval = interval;
            Metadata = metadata;
            EventType = EventType.Gauge;
        }

        // Copied from dotnet-counters
        public CounterPayload(string providerName, string name, string displayName, string displayUnits, string metadata, double value, DateTime timestamp, string type, EventType eventType)
        {
            Provider = providerName;
            Name = name;
            Metadata = metadata;
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

        public string Metadata { get; }

        public EventType EventType { get; set; }
    }

    internal class GaugePayload : CounterPayload
    {
        public GaugePayload(string providerName, string name, string displayName, string displayUnits, string metadata, double value, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, metadata, value, timestamp, "Metric", EventType.Gauge)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }
    }

    internal class CounterEndedPayload : CounterPayload
    {
        public CounterEndedPayload(string providerName, string name, string displayName, DateTime timestamp)
            : base(providerName, name, displayName, string.Empty, null, 0.0, timestamp, "Metric", EventType.CounterEnded)
        {

        }
    }

    internal class RatePayload : CounterPayload
    {
        public RatePayload(string providerName, string name, string displayName, string displayUnits, string metadata, double value, double intervalSecs, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, metadata, value, timestamp, "Rate", EventType.Rate)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            string unitsName = string.IsNullOrEmpty(displayUnits) ? "Count" : displayUnits;
            string intervalName = intervalSecs.ToString() + " sec";
            DisplayName = $"{counterName} ({unitsName} / {intervalName})";
        }
    }

    internal class PercentilePayload : CounterPayload
    {
        public PercentilePayload(string providerName, string name, string displayName, string displayUnits, string metadata, IEnumerable<Quantile> quantiles, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, metadata, 0.0, timestamp, "Metric", EventType.Histogram)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
            Quantiles = quantiles.ToArray();
        }

        public Quantile[] Quantiles { get; }
    }

    internal record struct Quantile(double Percentage, double Value);

    internal class ErrorPayload : CounterPayload
    {
        public ErrorPayload(string errorMessage) : this(errorMessage, DateTime.UtcNow) 
        {
        }

        public ErrorPayload(string errorMessage, DateTime timestamp) :
            base(string.Empty, string.Empty, string.Empty, string.Empty, null, 0.0, timestamp, "Metric", EventType.Error)
        {
            ErrorMessage = errorMessage;
        }

        public string ErrorMessage { get; private set; }
    }

    internal enum EventType : int
    {
        Rate,
        Gauge,
        Histogram,
        Error,
        CounterEnded
    }
}