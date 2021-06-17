// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
            Values = new KeyValuePair<string, double>[] { new KeyValuePair<string, double>("", value) };
            Timestamp = timestamp;
            CounterType = type;
        }

        public CounterPayload(string providerName, string name, string displayName, string displayUnits, string tags, KeyValuePair<string, double>[] values, DateTime timestamp, string type)
        {
            ProviderName = providerName;
            Name = name;
            Tags = tags;
            Values = values;
            Timestamp = timestamp;
            CounterType = type;
        }

        public string ProviderName { get; private set; }
        public string Name { get; private set; }
        public KeyValuePair<string,double>[] Values { get; private set; }
        public virtual string DisplayName { get; protected set; }
        public string CounterType { get; private set; }
        public DateTime Timestamp { get; private set; }
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
        public PercentilePayload(string providerName, string name, string displayName, string displayUnits, string tags, string quantiles, DateTime timestamp) :
            base(providerName, name, displayName, displayUnits, tags, ParseQuantiles(quantiles), timestamp, "Rate")
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }

        static KeyValuePair<string,double>[] ParseQuantiles(string quantileList)
        {
            string[] quantileParts = quantileList.Split(';', StringSplitOptions.RemoveEmptyEntries);
            List<KeyValuePair<string, double>> quantiles = new List<KeyValuePair<string, double>>();
            foreach(string quantile in quantileParts)
            {
                string[] keyValParts = quantile.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if(keyValParts.Length != 2)
                {
                    continue;
                }
                if(!double.TryParse(keyValParts[0], out double key))
                {
                    continue;
                }
                if(!double.TryParse(keyValParts[1], out double val))
                {
                    continue;
                }
                string formattedKey = $"P{key * 100}";
                quantiles.Add(new KeyValuePair<string, double>(formattedKey, val));
            }
            return quantiles.ToArray();
        }
    }
}
