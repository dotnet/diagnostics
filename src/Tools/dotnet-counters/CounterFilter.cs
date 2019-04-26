// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;


namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterFilter
    {
        private Dictionary<string, List<string>> enabledCounters;

        public CounterFilter()
        {
            enabledCounters = new Dictionary<string, List<string>>();
        }

        public void AddFilter(string providerName, string[] counters)
        {
            enabledCounters[providerName] = new List<string>(counters);
        }

        public bool Filter(string providerName, string counterName)
        {
            return enabledCounters.ContainsKey(providerName) && enabledCounters[providerName].Contains(counterName);
        }
    }
}
