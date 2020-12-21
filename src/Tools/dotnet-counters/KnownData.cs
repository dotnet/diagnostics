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

        private static readonly string net50 = "5.0";
        private static readonly string net31 = "3.1";
        private static readonly string net30 = "3.0";

        private static IEnumerable<CounterProvider> CreateKnownProviders(string runtimeVersion)
        {
            yield return new CounterProvider(
                "System.Runtime", // Name
                "A default set of performance counters provided by the .NET runtime.", // Description
                "0xffffffff", // Keywords
                "5", // Level 
                new[] { // Counters
                    new CounterProfile{ Name="cpu-usage", Description="The percent of process' CPU usage relative to all of the system CPU resource", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="working-set", Description="Amount of working set used by the process (MB)", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="gc-heap-size", Description="Total heap size reported by the GC (MB)", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="gen-0-gc-count", Description="Number of Gen 0 GCs between update intervals", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="gen-1-gc-count", Description="Number of Gen 1 GCs between update intervals", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="gen-2-gc-count", Description="Number of Gen 2 GCs between update intervals", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="time-in-gc", Description="% time in GC since the last GC", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="gen-0-size", Description="Gen 0 Heap Size", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="gen-1-size", Description="Gen 1 Heap Size", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="gen-2-size", Description="Gen 2 Heap Size", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="loh-size", Description="LOH Size", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="poh-size", Description="POH (Pinned Object Heap) Size", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="alloc-rate", Description="Number of bytes allocated in the managed heap between update intervals", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="gc-fragmentation", Description="GC Heap Fragmentation", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="assembly-count", Description="Number of Assemblies Loaded", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="exception-count", Description="Number of Exceptions / sec", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="threadpool-thread-count", Description="Number of ThreadPool Threads", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="monitor-lock-contention-count", Description="Number of times there were contention when trying to take the monitor lock between update intervals", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="threadpool-queue-length", Description="ThreadPool Work Items Queue Length", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="threadpool-completed-items-count", Description="ThreadPool Completed Work Items Count", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="active-timer-count", Description="Number of timers that are currently active", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="il-bytes-jitted", Description="Total IL bytes jitted", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="methods-jitted-count", Description="Number of methods jitted", SupportedVersions=new[] { net50 } }
                },
                runtimeVersion // RuntimeVersion
            );
            yield return new CounterProvider(
                "Microsoft.AspNetCore.Hosting", // Name
                "A set of performance counters provided by ASP.NET Core.", // Description
                "0x0", // Keywords
                "4", // Level 
                new[] { // Counters
                    new CounterProfile{ Name="requests-per-second", Description="Number of requests between update intervals", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="total-requests", Description="Total number of requests", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="current-requests", Description="Current number of requests", SupportedVersions=new[] { net30, net31, net50 } },
                    new CounterProfile{ Name="failed-requests", Description="Failed number of requests", SupportedVersions=new[] { net30, net31, net50 } },
                },
                runtimeVersion
            );
            yield return new CounterProvider(
                "Microsoft-AspNetCore-Server-Kestrel", // Name
                "A set of performance counters provided by Kestrel.", // Description
                "0x0", // Keywords
                "4", // Level
                new[] {
                    new CounterProfile{ Name="connections-per-second", Description="Number of connections between update intervals", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="total-connections", Description="Total Connections", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="tls-handshakes-per-second", Description="Number of TLS Handshakes made between update intervals", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="total-tls-handshakes", Description="Total number of TLS handshakes made", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="current-tls-handshakes", Description="Number of currently active TLS handshakes", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="failed-tls-handshakes", Description="Total number of failed TLS handshakes", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="current-connections", Description="Number of current connections", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="connection-queue-length", Description="Length of Kestrel Connection Queue", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="request-queue-length", Description="Length total HTTP request queue", SupportedVersions=new[] { net50 } },
                },
                runtimeVersion
            );
            yield return new CounterProvider(
                "System.Net.Http",
                "A set of performance counters for System.Net.Http",
                "0x0", // Keywords
                "4", // Level
                new[] {
                    new CounterProfile{ Name="requests-started", Description="Total Requests Started", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="requests-started-rate", Description="Number of Requests Started between update intervals", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="requests-aborted", Description="Total Requests Aborted", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="requests-aborted-rate", Description="Number of Requests Aborted between update intervals", SupportedVersions=new[] { net50 } },
                    new CounterProfile{ Name="current-requests", Description="Current Requests", SupportedVersions=new[] { net50 } }
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
