// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterSet
    {
        // a mapping from provider to a list of counters that should be enabled
        // an empty List means all counters are enabled
        private Dictionary<string, List<string>> _providerCounters;

        public CounterSet()
        {
            _providerCounters = new Dictionary<string, List<string>>();
        }

        public bool IsEmpty => _providerCounters.Count == 0;

        public IEnumerable<string> Providers => _providerCounters.Keys;
        
        public bool IncludesAllCounters(string providerName)
        {
            return _providerCounters.TryGetValue(providerName, out List<string> enabledCounters) && enabledCounters.Count == 0;
        }

        public IEnumerable<string> GetCounters(string providerName)
        {
            if (!_providerCounters.TryGetValue(providerName, out List<string> enabledCounters))
            {
                return Array.Empty<string>();
            }
            return enabledCounters;
        }

        // Called when we want to enable all counters under a provider name.
        public void AddAllProviderCounters(string providerName)
        {
            _providerCounters[providerName] = new List<string>();
        }

        public void AddProviderCounters(string providerName, string[] counters)
        {
            if(!_providerCounters.TryGetValue(providerName, out List<string> enabledCounters))
            {
                enabledCounters = new List<string>(counters.Distinct());
                _providerCounters.Add(providerName, enabledCounters);
            }
            else if(enabledCounters.Count != 0) // empty list means all counters are enabled already
            {
                foreach(string counter in counters)
                {
                    if(!enabledCounters.Contains(counter))
                    {
                        enabledCounters.Add(counter);
                    }
                }
            }
        }

        public bool Contains(string providerName, string counterName)
        {
            return _providerCounters.TryGetValue(providerName, out List<string> counters) && 
                (counters.Count == 0 || counters.Contains(counterName));
        }
    }
}
