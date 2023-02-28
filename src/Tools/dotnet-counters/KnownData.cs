// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Counters
{
    internal static class KnownData
    {
        private const string maxVersion = "8.0";

        internal static readonly string[] s_AllVersions = new[] { net30, net31, net50, net60, net70, net80 };
        private static readonly string[] s_StartingNet5 = new[] { net50, net60, net70, net80 };
        private static readonly string[] s_StartingNet6 = new[] { net60, net70, net80 };
        private static readonly string[] s_StartingNet7 = new[] { net70, net80 };

        private static readonly IReadOnlyDictionary<string, CounterProvider> _knownProviders =
            CreateKnownProviders(maxVersion).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        private const string net80 = "8.0";
        private const string net70 = "7.0";
        private const string net60 = "6.0";
        private const string net50 = "5.0";
        private const string net31 = "3.1";
        private const string net30 = "3.0";

        private static IEnumerable<CounterProvider> CreateKnownProviders(string runtimeVersion)
        {
            yield return new CounterProvider(
                "System.Runtime", // Name
                "A default set of performance counters provided by the .NET runtime.", // Description
                "0xffffffff", // Keywords
                "5", // Level
                new[] { // Counters
                    new CounterProfile{ Name="cpu-usage", Description="The percent of process' CPU usage relative to all of the system CPU resources [0-100]", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="working-set", Description="Amount of working set used by the process (MB)", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="gc-heap-size", Description="Total heap size reported by the GC (MB)", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="gen-0-gc-count", Description="Number of Gen 0 GCs between update intervals", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="gen-1-gc-count", Description="Number of Gen 1 GCs between update intervals", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="gen-2-gc-count", Description="Number of Gen 2 GCs between update intervals", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="time-in-gc", Description="% time in GC since the last GC", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="gen-0-size", Description="Gen 0 Heap Size", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="gen-1-size", Description="Gen 1 Heap Size", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="gen-2-size", Description="Gen 2 Heap Size", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="loh-size", Description="LOH Size", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="poh-size", Description="POH (Pinned Object Heap) Size", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="alloc-rate", Description="Number of bytes allocated in the managed heap between update intervals", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="gc-fragmentation", Description="GC Heap Fragmentation", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="assembly-count", Description="Number of Assemblies Loaded", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="exception-count", Description="Number of Exceptions / sec", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="threadpool-thread-count", Description="Number of ThreadPool Threads", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="monitor-lock-contention-count", Description="Number of times there were contention when trying to take the monitor lock between update intervals", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="threadpool-queue-length", Description="ThreadPool Work Items Queue Length", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="threadpool-completed-items-count", Description="ThreadPool Completed Work Items Count", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="active-timer-count", Description="Number of timers that are currently active", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="il-bytes-jitted", Description="Total IL bytes jitted", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="methods-jitted-count", Description="Number of methods jitted", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="gc-committed", Description="Size of committed memory by the GC (MB)", SupportedVersions=s_StartingNet6 }
                },
                runtimeVersion // RuntimeVersion
            );
            yield return new CounterProvider(
                "Microsoft.AspNetCore.Hosting", // Name
                "A set of performance counters provided by ASP.NET Core.", // Description
                "0x0", // Keywords
                "4", // Level
                new[] { // Counters
                    new CounterProfile{ Name="requests-per-second", Description="Number of requests between update intervals", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="total-requests", Description="Total number of requests", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="current-requests", Description="Current number of requests", SupportedVersions=s_AllVersions },
                    new CounterProfile{ Name="failed-requests", Description="Failed number of requests", SupportedVersions=s_AllVersions },
                },
                runtimeVersion
            );
            yield return new CounterProvider(
                "Microsoft-AspNetCore-Server-Kestrel", // Name
                "A set of performance counters provided by Kestrel.", // Description
                "0x0", // Keywords
                "4", // Level
                new[] {
                    new CounterProfile{ Name="connections-per-second", Description="Number of connections between update intervals", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="total-connections", Description="Total Connections", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="tls-handshakes-per-second", Description="Number of TLS Handshakes made between update intervals", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="total-tls-handshakes", Description="Total number of TLS handshakes made", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="current-tls-handshakes", Description="Number of currently active TLS handshakes", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="failed-tls-handshakes", Description="Total number of failed TLS handshakes", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="current-connections", Description="Number of current connections", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="connection-queue-length", Description="Length of Kestrel Connection Queue", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="request-queue-length", Description="Length total HTTP request queue", SupportedVersions=s_StartingNet5 },
                },
                runtimeVersion
            );
            yield return new CounterProvider(
                "System.Net.Http",
                "A set of performance counters for System.Net.Http",
                "0x0", // Keywords
                "1", // Level
                new[] {
                    new CounterProfile{ Name="requests-started", Description="Total Requests Started", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="requests-started-rate", Description="Number of Requests Started between update intervals", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="requests-aborted", Description="Total Requests Aborted", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="requests-aborted-rate", Description="Number of Requests Aborted between update intervals", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="current-requests", Description="Current Requests", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="http11-connections-current-total", Description="Current number of HTTP 1.1 connections", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="http20-connections-current-total", Description="Current number of HTTP 2.0 connections", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="http30-connections-current-total", Description="Current number of HTTP 3.0 connections", SupportedVersions=s_StartingNet7 },
                    new CounterProfile{ Name="http11-requests-queue-duration", Description="Average duration of the time HTTP 1.1 requests spent in the request queue", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="http20-requests-queue-duration", Description="Average duration of the time HTTP 2.0 requests spent in the request queue", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="http30-requests-queue-duration", Description="Average duration of the time HTTP 3.0 requests spent in the request queue", SupportedVersions=s_StartingNet7 },
                },
                runtimeVersion
            );
            yield return new CounterProvider(
                "System.Net.NameResolution",
                "A set of performance counters for DNS lookups",
                "0x0",
                "1",
                new[] {
                    new CounterProfile{ Name="dns-lookups-requested", Description="The number of DNS lookups requested since the process started", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="dns-lookups-duration", Description="Average DNS Lookup Duration", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="current-dns-lookups", Description="The current number of DNS lookups that have started but not yet completed", SupportedVersions=s_StartingNet6 },
                },
                runtimeVersion
            );
            yield return new CounterProvider(
                "System.Net.Security",
                "A set of performance counters for TLS",
                "0x0",
                "1",
                new[] {
                    new CounterProfile{ Name="tls-handshake-rate", Description="The number of TLS handshakes completed per update interval", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="total-tls-handshakes", Description="The total number of TLS handshakes completed since the process started", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="current-tls-handshakes", Description="The current number of TLS handshakes that have started but not yet completed", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="failed-tls-handshakes", Description="The total number of TLS handshakes failed since the process started", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="all-tls-sessions-open", Description="The number of active all TLS sessions", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="tls10-sessions-open", Description="The number of active TLS 1.0 sessions", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="tls11-sessions-open", Description="The number of active TLS 1.1 sessions", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="tls12-sessions-open", Description="The number of active TLS 1.2 sessions", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="tls13-sessions-open", Description="The number of active TLS 1.3 sessions", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="all-tls-handshake-duration", Description="The average duration of all TLS handshakes", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="tls10-handshake-duration", Description="The average duration of TLS 1.0 handshakes", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="tls11-handshake-duration", Description="The average duration of TLS 1.1 handshakes", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="tls12-handshake-duration", Description="The average duration of TLS 1.2 handshakes", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="tls13-handshake-duration", Description="The average duration of TLS 1.3 handshakes", SupportedVersions=s_StartingNet5 },
                },
                runtimeVersion
            );
            yield return new CounterProvider(
                "System.Net.Sockets",
                "A set of performance counters for System.Net.Sockets",
                "0x0",
                "1",
                new[] {
                    new CounterProfile{ Name="outgoing-connections-established", Description="The total number of outgoing connections established since the process started", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="incoming-connections-established", Description="The total number of incoming connections established since the process started", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="current-outgoing-connect-attempts", Description="The current number of outgoing connect attempts that have started but not yet completed", SupportedVersions=s_StartingNet7 },
                    new CounterProfile{ Name="bytes-received", Description="The total number of bytes received since the process started", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="bytes-sent", Description="The total number of bytes sent since the process started", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="datagrams-received", Description="The total number of datagrams received since the process started", SupportedVersions=s_StartingNet5 },
                    new CounterProfile{ Name="datagrams-sent", Description="The total number of datagrams sent since the process started", SupportedVersions=s_StartingNet5 },
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