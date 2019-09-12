// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
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
                description: "Lists pre-built tracing profiles with a description of what providers and filters are in each profile.",
                handler: CommandHandler.Create<IConsole>(GetProfiles),
                isHidden: false);

        // FIXME: Read from a config file!
        internal static IEnumerable<Profile> DotNETRuntimeProfiles { get; } = new[] {
            new Profile(
                "cpu-sampling",
                new Provider[] {
                    new Provider("Microsoft-DotNETCore-SampleProfiler"),
                    new Provider("Microsoft-Windows-DotNETRuntime", (ulong)ClrTraceEventParser.Keywords.Default, EventLevel.Informational),
                },
                "Useful for tracking CPU usage and general runtime information. This the default option if no profile is specified."),
            new Profile(
                "gc-verbose",
                new Provider[] {
                    new Provider(
                        name: "Microsoft-Windows-DotNETRuntime",
                        keywords: (ulong)ClrTraceEventParser.Keywords.GC |
                                  (ulong)ClrTraceEventParser.Keywords.GCHandle |
                                  (ulong)ClrTraceEventParser.Keywords.Exception,
                        eventLevel: EventLevel.Verbose
                    ),
                },
                "Tracks GC and GC handle events at verbose level, and samples the allocation events as well."),
            new Profile(
                "gc-collect",
                new Provider[] {
                    new Provider(
                        name: "Microsoft-Windows-DotNETRuntime",
                        keywords:   (ulong)ClrTraceEventParser.Keywords.GC |
                                    (ulong)ClrTraceEventParser.Keywords.Exception,
                        eventLevel: EventLevel.Informational),
                },
                "Tracks GC collection only at very low overhead."),
        };
    }
}
