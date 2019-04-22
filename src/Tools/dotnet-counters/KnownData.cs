// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Microsoft.Diagnostics.Tools.Counters
{
    internal static class KnownData
    {
        private static readonly IReadOnlyDictionary<string, CounterProvider> _knownProviders =
            CreateKnownProviders().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);


        private static IEnumerable<CounterProvider> CreateKnownProviders()
        {
            yield return new CounterProvider(
                "System.Runtime", // Name
                "A default set of performance counters provided by the .NET runtime.", // Description
                "0xffffffff", // Keywords
                "0x5", // Level 
                new[] { // Counters
                    // NOTE: For now, the set of counters below doesn't really matter because 
                    // we don't really display any counters in real time. (We just collect .netperf files) 
                    // In the future (with IPC), we should filter counter payloads by name provided below to display.  
                    // These are mainly here as placeholders. 
                    new CounterProfile{ Name="total-processor-time", Description="Amount of time the process has utilized the CPU (ms)" },
                    new CounterProfile{ Name="private-memory", Description="Amount of private virtual memory used by the process (KB)" },
                    new CounterProfile{ Name="working-set", Description="Amount of working set used by the process (KB)" },
                    new CounterProfile{ Name="virtual-memory", Description="Amount of virtual memory used by the process (KB)" },
                    new CounterProfile{ Name="gc-total-memory", Description="Amount of committed virtual memory used by the GC (KB)" },
                    new CounterProfile{ Name="exceptions-thrown-rate", Description="Number of exceptions thrown in a recent 1 minute window (exceptions/min)" },
                });

            // TODO: Add more providers (ex. ASP.NET ones)
        }

        public static IReadOnlyList<CounterProvider> GetAllProviders() => _knownProviders.Values.ToList();

        public static bool TryGetProvider(string providerName, out CounterProvider provider) => _knownProviders.TryGetValue(providerName, out provider);
    }
}
