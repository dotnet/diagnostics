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
                        keywords:   (long)ClrTraceEventParser.Keywords.GC |
                                    (long)ClrTraceEventParser.Keywords.Exception
                    )
                },
                "Tracks GC collections only at very low overhead."),
        };
    }
}
