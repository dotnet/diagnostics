// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal sealed class ListProfilesCommandHandler
    {
        /// <summary>
        /// Indicates diagnostics messages from DiagnosticSourceEventSource should be included.
        /// </summary>
        /// <remarks>See: https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/DiagnosticSourceEventSource.cs</remarks>
        private const long DiagnosticSourceKeywords_Messages = 0x1;
          
        /// <summary>
        /// Indicates that all events from all diagnostic sources should be forwarded to the EventSource using the 'Event' event.
        /// </summary>
        /// <remarks>See: https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/DiagnosticSourceEventSource.cs</remarks>
        private const long DiagnosticSourceKeywords_Events = 0x2;

        public static async Task<int> GetProfiles(IConsole console)
        {
            try
            {
                foreach (var profile in DotNETRuntimeProfiles)
                    Console.Out.WriteLine($"\t{profile.Name,-16} - {profile.Description}");

                await Task.FromResult(0);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return 1;
            }
        }

        public static Command ListProfilesCommand() =>
            new Command(
                name: "list-profiles",
                description: "Lists pre-built tracing profiles with a description of what providers and filters are in each profile")
            {
                Handler = CommandHandler.Create<IConsole>(GetProfiles),
            };

        internal static IEnumerable<Profile> DotNETRuntimeProfiles { get; } = new[] {
            new Profile(
                "cpu-sampling",
                new EventPipeProvider[] {
                    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.Default)
                },
                "Useful for tracking CPU usage and general .NET runtime information. This is the default option if no profile or providers are specified."),
            new Profile(
                "gc-verbose",
                new EventPipeProvider[] {
                    new EventPipeProvider(
                        name: "Microsoft-Windows-DotNETRuntime",
                        eventLevel: EventLevel.Verbose,
                        keywords: (long)ClrTraceEventParser.Keywords.GC |
                                  (long)ClrTraceEventParser.Keywords.GCHandle |
                                  (long)ClrTraceEventParser.Keywords.Exception
                    ),
                },
                "Tracks GC collections and samples object allocations."),
            new Profile(
                "gc-collect",
                new EventPipeProvider[] {
                    new EventPipeProvider(
                        name: "Microsoft-Windows-DotNETRuntime",
                        eventLevel: EventLevel.Informational,
                        keywords:   (long)ClrTraceEventParser.Keywords.GC
                    )
                },
                "Tracks GC collections only at very low overhead."),
            new Profile(
                "aspnet-requests",
                new EventPipeProvider[] {
                    new EventPipeProvider(
                        name: "System.Threading.Tasks.TplEventSource",
                        eventLevel: EventLevel.Verbose,
                        keywords:   (long)TplEtwProviderTraceEventParser.Keywords.Tasktransfer |
                                    (long)TplEtwProviderTraceEventParser.Keywords.Tasks |
                                    (long)TplEtwProviderTraceEventParser.Keywords.Taskstops |
                                    (long)TplEtwProviderTraceEventParser.Keywords.TasksFlowActivityIds |
                                    (long)TplEtwProviderTraceEventParser.Keywords.Asynccausalityoperation | 
                                    (long)TplEtwProviderTraceEventParser.Keywords.Asynccausalityrelation
                    ),
                    new EventPipeProvider(
                        name: "Microsoft-Diagnostics-DiagnosticSource",
                        eventLevel: EventLevel.Verbose,
                        keywords: DiagnosticSourceKeywords_Messages |
                                  DiagnosticSourceKeywords_Events,
                        arguments: new Dictionary<string, string> {
                            { 
                                "FilterAndPayloadSpecs",
                                    "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-TraceIdentifier;Request.Method;Request.Host;Request.Path;Request.QueryString\r\n" + 
                                    "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop@Activity1Stop:-TraceIdentifier;Response.StatusCode"
                            }
                        }
                    )
                },
                "Captures ASP.NET requests"
            ),
            new Profile(
                "database",
                new EventPipeProvider[] {
                    new EventPipeProvider(
                        name: "System.Threading.Tasks.TplEventSource",
                        eventLevel: EventLevel.Verbose,
                        keywords:   (long)TplEtwProviderTraceEventParser.Keywords.Tasktransfer |
                                    (long)TplEtwProviderTraceEventParser.Keywords.Tasks |
                                    (long)TplEtwProviderTraceEventParser.Keywords.Taskstops |
                                    (long)TplEtwProviderTraceEventParser.Keywords.TasksFlowActivityIds
                    ),
                    new EventPipeProvider(
                        name: "Microsoft-Diagnostics-DiagnosticSource",
                        eventLevel: EventLevel.Verbose,
                        keywords: DiagnosticSourceKeywords_Messages |
                                  DiagnosticSourceKeywords_Events,
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
    }
}
