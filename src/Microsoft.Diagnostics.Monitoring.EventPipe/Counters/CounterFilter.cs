// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal sealed class CounterFilter
    {
        private Dictionary<string, (int? IntervalMilliseconds, List<string> CounterNames)> _enabledCounters;
        private int _defaultIntervalMilliseconds;

        public static CounterFilter AllCounters(float defaultIntervalSeconds)
            => new CounterFilter(defaultIntervalSeconds);

        public CounterFilter(float defaultIntervalSeconds)
        {
            //Provider names are not case sensitive, but counter names are.
            _enabledCounters = new(StringComparer.OrdinalIgnoreCase);

            //The Series payload of the counter which we use for filtering
            _defaultIntervalMilliseconds = SecondsToMilliseconds(defaultIntervalSeconds);
        }

        // Called when we want to enable all counters under a provider name.
        public void AddFilter(string providerName)
        {
            AddFilter(providerName, Array.Empty<string>());
        }

        public void AddFilter(string providerName, string[] counters, float? intervalSeconds = null)
        {
            _enabledCounters[providerName] = (
                IntervalMilliseconds: (intervalSeconds.HasValue ? SecondsToMilliseconds(intervalSeconds.Value) : null),
                CounterNames: new List<string>(counters ?? Array.Empty<string>()));
        }

        public IEnumerable<string> GetProviders() => _enabledCounters.Keys;

        public int DefaultIntervalSeconds => _defaultIntervalMilliseconds / 1000;

        public bool IsIncluded(string providerName, string counterName, int intervalMilliseconds)
        {
            int requestedInterval = _defaultIntervalMilliseconds;
            if (_enabledCounters.TryGetValue(providerName, out (int? IntervalMilliseconds, List<string> CounterNames) enabledCounters))
            {
                if (enabledCounters.IntervalMilliseconds.HasValue)
                {
                    requestedInterval = enabledCounters.IntervalMilliseconds.Value;
                }
            }
            if (requestedInterval != intervalMilliseconds)
            {
                return false;
            }

            return IsIncluded(providerName, counterName);
        }

        public bool IsIncluded(string providerName, string counterName)
        {
            if (_enabledCounters.Count == 0)
            {
                return true;
            }
            if (_enabledCounters.TryGetValue(providerName, out (int? IntervalMilliseconds, List<string> CounterNames) enabledCounters))
            {
                return enabledCounters.CounterNames.Count == 0 || enabledCounters.CounterNames.Contains(counterName, StringComparer.Ordinal);
            }
            return false;
        }

        private static int SecondsToMilliseconds(float intervalSeconds) => (int)(intervalSeconds * 1000);
    }
}
