﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal sealed class ListProfilesCommandHandler
    {
        private static long defaultKeyword =    0x1 |           // GC
                                                0x4 |           // Loader
                                                0x8 |           // AssemblyLoader
                                                0x10 |          // JIT
                                                0x8000 |        // Exceptions
                                                0x10000 |       // Threading
                                                0x20000 |       // JittedMethodILToNativeMap
                                                0x1000000000;   // Contention

        private static string dotnetCommonDescription = """
                                                        Lightweight .NET runtime diagnostics designed to stay low overhead.
                                                        Includes:
                                                            GC
                                                            AssemblyLoader
                                                            Jit
                                                            Exception
                                                            Threading
                                                            JittedMethodILToNativeMap
                                                            Compilation
                                                            Contention
                                                        Equivalent to --providers "Microsoft-Windows-DotNETRuntime:0x100003801D:4".
                                                        """;

        public static int GetProfiles()
        {
            try
            {
                Console.Out.WriteLine("dotnet-trace collect profiles:");
                int profileNameWidth = ProfileNamesMaxWidth(DotNETRuntimeProfiles);
                foreach (Profile profile in DotNETRuntimeProfiles)
                {
                    PrintProfile(profile, profileNameWidth);
                }

                if (OperatingSystem.IsLinux())
                {
                    Console.Out.WriteLine("\ndotnet-trace collect-linux profiles:");
                    profileNameWidth = Math.Max(profileNameWidth, ProfileNamesMaxWidth(LinuxPerfEventProfiles));
                    foreach (Profile profile in DotNETRuntimeProfiles)
                    {
                        PrintProfile(profile, profileNameWidth);
                    }
                    foreach (Profile profile in LinuxPerfEventProfiles)
                    {
                        PrintProfile(profile, profileNameWidth);
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex}");
                return 1;
            }
        }

        public static Command ListProfilesCommand()
        {
            Command listProfilesCommand = new(
                name: "list-profiles",
                description: "Lists pre-built tracing profiles with a description of what providers and filters are in each profile");

            listProfilesCommand.SetAction((parseResult, ct) => Task.FromResult(GetProfiles()));
            return listProfilesCommand;
        }

        internal static IEnumerable<Profile> DotNETRuntimeProfiles { get; } = new[] {
            new Profile(
                "dotnet-common",
                new EventPipeProvider[] {
                    new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, defaultKeyword)
                },
                dotnetCommonDescription),
            new Profile(
                "dotnet-sampled-thread-time",
                new EventPipeProvider[] {
                    new("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
                },
                "Samples .NET thread stacks (~100 Hz) to identify hotspots over time. Uses the runtime sample profiler with managed stacks."),
            new Profile(
                "gc-verbose",
                new EventPipeProvider[] {
                    new(
                        name: "Microsoft-Windows-DotNETRuntime",
                        eventLevel: EventLevel.Verbose,
                        keywords: (long)ClrTraceEventParser.Keywords.GC |
                                  (long)ClrTraceEventParser.Keywords.GCHandle |
                                  (long)ClrTraceEventParser.Keywords.Exception
                    )
                },
                "Tracks GC collections and samples object allocations."),
            new Profile(
                "gc-collect",
                new EventPipeProvider[] {
                    new(
                        name: "Microsoft-Windows-DotNETRuntime",
                        eventLevel: EventLevel.Informational,
                        keywords: (long)ClrTraceEventParser.Keywords.GC
                    ),
                    new(
                        name: "Microsoft-Windows-DotNETRuntimePrivate",
                        eventLevel: EventLevel.Informational,
                        keywords: (long)ClrTraceEventParser.Keywords.GC
                    )
                },
                "Tracks GC collections only at very low overhead.") { RundownKeyword = (long)ClrTraceEventParser.Keywords.GC, RetryStrategy = RetryStrategy.DropKeywordDropRundown },
            new Profile(
                "database",
                new EventPipeProvider[] {
                    new(
                        name: "System.Threading.Tasks.TplEventSource",
                        eventLevel: EventLevel.Informational,
                        keywords: (long)TplEtwProviderTraceEventParser.Keywords.TasksFlowActivityIds
                    ),
                    new(
                        name: "Microsoft-Diagnostics-DiagnosticSource",
                        eventLevel: EventLevel.Verbose,
                        keywords:   (long)DiagnosticSourceKeywords.Messages |
                                    (long)DiagnosticSourceKeywords.Events,
                        arguments: new Dictionary<string, string> {
                            {
                                "FilterAndPayloadSpecs",
                                    "SqlClientDiagnosticListener/System.Data.SqlClient.WriteCommandBefore@Activity1Start:-Command;Command.CommandText;ConnectionId;Operation;Command.Connection.ServerVersion;Command.CommandTimeout;Command.CommandType;Command.Connection.ConnectionString;Command.Connection.Database;Command.Connection.DataSource;Command.Connection.PacketSize\r\n" +
                                    "SqlClientDiagnosticListener/System.Data.SqlClient.WriteCommandAfter@Activity1Stop:\r\n" +
                                    "Microsoft.EntityFrameworkCore/Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting@Activity2Start:-Command.CommandText;Command;ConnectionId;IsAsync;Command.Connection.ClientConnectionId;Command.Connection.ServerVersion;Command.CommandTimeout;Command.CommandType;Command.Connection.ConnectionString;Command.Connection.Database;Command.Connection.DataSource;Command.Connection.PacketSize\r\n" +
                                    "Microsoft.EntityFrameworkCore/Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted@Activity2Stop:"
                            }
                        }
                    )
                },
                "Captures ADO.NET and Entity Framework database commands")
        };

        internal static IEnumerable<Profile> LinuxPerfEventProfiles { get; } = new[] {
            new Profile(
                "kernel-cpu",
                providers: Array.Empty<EventPipeProvider>(),
                description: "Kernel CPU sampling (perf-based), emitted as Universal.Events/cpu, for precise on-CPU attribution."),
            new Profile(
                "kernel-cswitch",
                providers: Array.Empty<EventPipeProvider>(),
                description: "Kernel thread context switches, emitted as Universal.Events/cswitch, for on/off-CPU and scheduler analysis.")
        };

        private static int ProfileNamesMaxWidth(IEnumerable<Profile> profiles)
        {
            int maxWidth = 0;
            foreach (Profile profile in profiles)
            {
                if (profile.Name.Length > maxWidth)
                {
                    maxWidth = profile.Name.Length;
                }
            }

            return maxWidth;
        }

        private static void PrintProfile(Profile profile, int nameColumnWidth)
        {
            string[] descriptionLines = profile.Description.Replace("\r\n", "\n").Split('\n');

            Console.Out.WriteLine($"\t{profile.Name.PadRight(nameColumnWidth)} - {descriptionLines[0]}");

            string continuationPrefix = $"\t{new string(' ', nameColumnWidth)}   ";
            for (int i = 1; i < descriptionLines.Length; i++)
            {
                Console.Out.WriteLine(continuationPrefix + descriptionLines[i]);
            }
        }

        /// <summary>
        /// Keywords for DiagnosticSourceEventSource provider
        /// </summary>
        /// <remarks>See https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/DiagnosticSourceEventSource.cs</remarks>
        private enum DiagnosticSourceKeywords : long
        {
            Messages = 0x1,
            Events = 0x2,
            IgnoreShortCutKeywords = 0x0800,
            AspNetCoreHosting = 0x1000,
            EntityFrameworkCoreCommands = 0x2000
        }
    }
}
