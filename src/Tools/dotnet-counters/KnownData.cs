// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
                "5", // Level 
                new[] { // Counters
                    new CounterProfile{ Name="cpu-usage", Description="Amount of time the process has utilized the CPU (ms)", DisplayName="CPU Usage (%)" },
                    new CounterProfile{ Name="working-set", Description="Amount of working set used by the process (MB)", DisplayName="Working Set (MB)" },
                    new CounterProfile{ Name="gc-heap-size", Description="Total heap size reported by the GC (MB)", DisplayName="GC Heap Size (MB)" },
                    new CounterProfile{ Name="gen-0-gc-count", Description="Number of Gen 0 GCs / sec", DisplayName="Gen 0 GC / sec" },
                    new CounterProfile{ Name="gen-1-gc-count", Description="Number of Gen 1 GCs / sec", DisplayName="Gen 1 GC / sec" },
                    new CounterProfile{ Name="gen-2-gc-count", Description="Number of Gen 2 GCs / sec", DisplayName="Gen 2 GC / sec" },
                    new CounterProfile{ Name="time-in-gc", Description="% time in GC since the last GC", DisplayName="% Time in GC (since last GC)" },
                    new CounterProfile{ Name="gen-0-size", Description="Gen 0 Heap Size", DisplayName="Gen 0 Size (B)" },
                    new CounterProfile{ Name="gen-1-size", Description="Gen 1 Heap Size", DisplayName="Gen 1 Size (B)" },
                    new CounterProfile{ Name="gen-2-size", Description="Gen 2 Heap Size", DisplayName="Gen 2 Size (B)" },
                    new CounterProfile{ Name="loh-size", Description="LOH Heap Size", DisplayName="LOH Size (B)" },
                    new CounterProfile{ Name="alloc-rate", Description="Allocation Rate", DisplayName="Allocation Rate (Bytes / sec)" },
                    new CounterProfile{ Name="assembly-count", Description="Number of Assemblies Loaded", DisplayName="# of Assemblies Loaded" },
                    new CounterProfile{ Name="exception-count", Description="Number of Exceptions / sec", DisplayName="Exceptions / sec" },
                    new CounterProfile{ Name="threadpool-thread-count", Description="Number of ThreadPool Threads", DisplayName="ThreadPool Threads Count" },
                    new CounterProfile{ Name="monitor-lock-contention-count", Description="Monitor Lock Contention Count", DisplayName="Monitor Lock Contention Count / sec" },
                    new CounterProfile{ Name="threadpool-queue-length", Description="ThreadPool Work Items Queue Length", DisplayName="ThreadPool Queue Length" },
                    new CounterProfile{ Name="threadpool-completed-items-count", Description="ThreadPool Completed Work Items Count", DisplayName="ThreadPool Completed Work Items / sec" },
                    new CounterProfile{ Name="active-timer-count", Description="Active Timers Count", DisplayName="Number of Active Timers" },
                });
            yield return new CounterProvider(
                "Microsoft.AspNetCore.Hosting", // Name
                "A set of performance counters provided by ASP.NET Core.", // Description
                "0x0", // Keywords
                "4", // Level 
                new[] { // Counters
                    new CounterProfile{ Name="requests-per-second", Description="Request rate", DisplayName="Request / sec" },
                    new CounterProfile{ Name="total-requests", Description="Total number of requests", DisplayName="Total Requests" },
                    new CounterProfile{ Name="current-requests", Description="Current number of requests", DisplayName="Current Requests" },
                    new CounterProfile{ Name="failed-requests", Description="Failed number of requests", DisplayName="Failed Requests" },
                });
        }

        public static IReadOnlyList<CounterProvider> GetAllProviders() => _knownProviders.Values.ToList();

        public static bool TryGetProvider(string providerName, out CounterProvider provider) => _knownProviders.TryGetValue(providerName, out provider);
    }
}
