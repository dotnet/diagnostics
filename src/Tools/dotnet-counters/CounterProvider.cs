// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterProvider
    {
        public string Name { get; }
        public string Description { get; }
        public string Keywords { get; }
        public string Level { get; }
        public Dictionary<string, CounterProfile> Counters { get; }

        public CounterProvider(string name, string description, string keywords, string level, IEnumerable<CounterProfile> counters)
        {
            Name = name;
            Description = description;
            Keywords = keywords;
            Level = level;
            Counters = new Dictionary<string, CounterProfile>();
            foreach (CounterProfile counter in counters)
            {
                Counters.Add(counter.Name, counter);
            }
        }

        public string ToProviderString(int interval)
        {
            return $"{Name}:{Keywords}:{Level}:EventCounterIntervalSec={interval}";
        }

        public static string SerializeUnknownProviderName(string unknownCounterProviderName, int interval)
        {
            return $"{unknownCounterProviderName}:ffffffff:4:EventCounterIntervalSec={interval}";
        }

        public IReadOnlyList<CounterProfile> GetAllCounters() => Counters.Values.ToList();

    }

    public class CounterProfile
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }
}
