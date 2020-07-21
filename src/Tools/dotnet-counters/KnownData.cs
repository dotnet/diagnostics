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
        private static readonly string maxVersion = "5.0";
        private static readonly IReadOnlyDictionary<string, CounterProvider> _knownProviders =
            CreateKnownProviders(maxVersion).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        private static IEnumerable<CounterProvider> CreateKnownProviders(string runtimeVersion)
        {
            yield return new CounterProvider(
                "System.Runtime", // Name
                "A default set of performance counters provided by the .NET runtime.", // Description
                "0xffffffff", // Keywords
                "5", // Level 
                new[] { // Counters
                    new CounterProfile{ Name="cpu-usage", Description="Amount of time the process has utilized the CPU (ms)", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="working-set", Description="Amount of working set used by the process (MB)", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="gc-heap-size", Description="Total heap size reported by the GC (MB)", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="gen-0-gc-count", Description="Number of Gen 0 GCs / min", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="gen-1-gc-count", Description="Number of Gen 1 GCs / min", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="gen-2-gc-count", Description="Number of Gen 2 GCs / min", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="time-in-gc", Description="% time in GC since the last GC", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="gen-0-size", Description="Gen 0 Heap Size", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="gen-1-size", Description="Gen 1 Heap Size", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="gen-2-size", Description="Gen 2 Heap Size", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="loh-size", Description="LOH Size", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="poh-size", Description="POH (Pinned Object Heap) Size", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="alloc-rate", Description="Number of bytes allocated in the managed heap per second", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="assembly-count", Description="Number of Assemblies Loaded", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="exception-count", Description="Number of Exceptions / sec", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="threadpool-thread-count", Description="Number of ThreadPool Threads", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="monitor-lock-contention-count", Description="Number of times there were contention when trying to take the monitor lock per second", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="threadpool-queue-length", Description="ThreadPool Work Items Queue Length", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="threadpool-completed-items-count", Description="ThreadPool Completed Work Items Count", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="active-timer-count", Description="Number of timers that are currently active", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="il-bytes-jitted", Description="Total IL bytes jitted", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="methods-jitted-count", Description="Number of methods jitted", SupportedVersions=new[] { "5.0" } }
                },
                runtimeVersion // RuntimeVersion
            );
            yield return new CounterProvider(
                "Microsoft.AspNetCore.Hosting", // Name
                "A set of performance counters provided by ASP.NET Core.", // Description
                "0x0", // Keywords
                "4", // Level 
                new[] { // Counters
                    new CounterProfile{ Name="requests-per-second", Description="Request rate", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="total-requests", Description="Total number of requests", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="current-requests", Description="Current number of requests", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                    new CounterProfile{ Name="failed-requests", Description="Failed number of requests", SupportedVersions=new[] { "3.0", "3.1", "5.0" } },
                },
                runtimeVersion
            );
            yield return new CounterProvider(
                "Microsoft-AspNetCore-Server-Kestrel", // Name
                "A set of performance counters provided by Kestrel.", // Description
                "0x0", // Keywords
                "4", // Level
                new[] {
                    new CounterProfile{ Name="connections-per-second", Description="Connection Rate", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="total-connections", Description="Total Connections", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="tls-handshakes-per-second", Description="Rate at which TLS Handshakes are made", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="total-tls-handshakes", Description="Total number of TLS handshakes made", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="current-tls-handshakes", Description="Number of currently active TLS handshakes", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="failed-tls-handshakes", Description="Total number of failed TLS handshakes", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="current-connections", Description="Number of current connections", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="connection-queue-length", Description="Length of Kestrel Connection Queue", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="request-queue-length", Description="Length total HTTP request queue", SupportedVersions=new[] { "5.0" } },
                },
                runtimeVersion
            );
            yield return new CounterProvider(
                "System.Net.Http",
                "A set of performance counters for System.Net.Http",
                "0x0", // Keywords
                "4", // Level
                new[] {
                    new CounterProfile{ Name="requests-started", Description="Requests Started", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="requests-started-rate", Description="Requests Started Rate", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="requests-aborted", Description="Requests Aborted", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="requests-aborted-rate", Description="Requests Aborted Rate", SupportedVersions=new[] { "5.0" } },
                    new CounterProfile{ Name="current-requests", Description="Current Requests", SupportedVersions=new[] { "5.0" } }
                },
                runtimeVersion
            );
        }

        public static IReadOnlyList<CounterProvider> GetAllProviders(string version)
        {
            return CreateKnownProviders(version).Where(p => p.Counters.Count > 0).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase).Values.ToList();
        }

        public static bool TryGetProvider(string providerName, out CounterProvider provider) => _knownProviders.TryGetValue(providerName, out provider);
    }
}
