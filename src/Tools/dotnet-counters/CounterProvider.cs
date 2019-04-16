// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public enum CounterType
    {
        EventCounter = 1,
        PollingCounter,
        IncrementingEventCounter,
        IncrementingPollingCounter,
    }

    public class CounterProvider
    {
        public static readonly string DefaultProviderName = "System.Runtime";

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
                Console.WriteLine($"Adding: {counter.Name}");
                Counters.Add(counter.Name, counter);
            }
        }

        public string ToProviderString(int interval)
        {
            return $"{Name}:{Keywords}:{Level}:EventCounterIntervalSec={interval}";
        }
    }

    public class CounterProfile
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public CounterType Type { get; set; }
    }
}
