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
        private Dictionary<string, List<string>> _enabledCounters;
        private int _intervalMilliseconds;

        public static CounterFilter AllCounters(float counterIntervalSeconds)
            => new CounterFilter(counterIntervalSeconds);

        public CounterFilter(float intervalSeconds)
        {
            //Provider names are not case sensitive, but counter names are.
            _enabledCounters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            //The Series payload of the counter which we use for filtering
            _intervalMilliseconds = (int)(intervalSeconds * 1000);
        }

        // Called when we want to enable all counters under a provider name.
        public void AddFilter(string providerName)
        {
            AddFilter(providerName, Array.Empty<string>());
        }

        public void AddFilter(string providerName, string[] counters)
        {
            _enabledCounters[providerName] = new List<string>(counters ?? Array.Empty<string>());
        }

        public IEnumerable<string> GetProviders() => _enabledCounters.Keys;

        public bool IsIncluded(string providerName, string counterName, int intervalMilliseconds)
        {
            if (_intervalMilliseconds != intervalMilliseconds)
            {
                return false;
            }
            if (_enabledCounters.Count == 0)
            {
                return true;
            }
            if (_enabledCounters.TryGetValue(providerName, out List<string> enabledCounters))
            {
                return enabledCounters.Count == 0 || enabledCounters.Contains(counterName, StringComparer.Ordinal);
            }
            return false;
        }
    }
}
