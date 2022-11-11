// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class CounterPayload : ICounterPayload
    {
        public CounterPayload(DateTime timestamp,
            string provider,
            string name,
            string displayName,
            string unit,
            double value,
            CounterType counterType,
            float interval)
        {
            Timestamp = timestamp;
            Name = name;
            DisplayName = displayName;
            Unit = unit;
            Value = value;
            CounterType = counterType;
            Provider = provider;
            Interval = interval;
        }

        // Copied from dotnet-counters
        public CounterPayload(string providerName, string name, string displayName, string displayUnits, string tags, double value, DateTime timestamp, string type)
        {
            Provider = providerName;
            Name = name;
            Tags = tags;
            Value = value;
            Timestamp = timestamp;
            CounterType = (CounterType)Enum.Parse(typeof(CounterType), type);
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

    }

    class GaugePayload : CounterPayload
    {
        public GaugePayload(string providerName, string name, string displayName, string displayUnits, string tags, double value, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, tags, value, timestamp, "Metric")
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }
    }

    class RatePayload : CounterPayload
    {
        public RatePayload(string providerName, string name, string displayName, string displayUnits, string tags, double value, double intervalSecs, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, tags, value, timestamp, "Rate")
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
        public PercentilePayload(string providerName, string name, string displayName, string displayUnits, string tags, double val, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, tags, val, timestamp, "Metric")
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }
    }
}