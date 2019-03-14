// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterProvider
    {
        public static readonly string DefaultProviderName = "System.Runtime";

        public string Name { get; }
        public string Description { get; }
        public string Keywords { get; }
        public string Level { get; }
        public IReadOnlyList<CounterProfile> Counters { get; }

        public CounterProvider(string name, string description, string keywords, string level, IEnumerable<CounterProfile> counters)
        {
            Name = name;
            Description = description;
            Keywords = keywords;
            Level = level;
            Counters = counters.ToList();
        }
    }

    public class CounterProfile
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }
}
