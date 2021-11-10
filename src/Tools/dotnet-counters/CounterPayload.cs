// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterPayload
    {
        public CounterPayload(string providerName, string name, string displayName, string displayUnits, string tags, double value, DateTime timestamp, string type)
        {
            ProviderName = providerName;
            Name = name;
            Tags = tags;
            Value = value;
            Timestamp = timestamp;
            CounterType = type;
        }

        public string ProviderName { get; private set; }
        public string Name { get; private set; }
        public double Value { get; private set; }
        public virtual string DisplayName { get; protected set; }
        public string CounterType { get; private set; }
        public DateTime Timestamp { get; private set; }
        public string Tags { get; private set; }
    }

    internal class GaugePayload : CounterPayload
    {
        public GaugePayload(string providerName, string name, string displayName, string displayUnits, string tags, double value, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, tags, value, timestamp, "Metric")
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }
    }

    internal class RatePayload : CounterPayload
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

    internal class PercentilePayload : CounterPayload
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
