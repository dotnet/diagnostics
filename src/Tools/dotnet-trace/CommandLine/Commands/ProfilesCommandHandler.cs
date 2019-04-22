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
    internal sealed class ProfilesCommandHandler
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

        public static Command ProfilesCommand() =>
            new Command(
                name: "profiles",
                description: "List pre-defined set of provider aliases that allows common tracing scenarios to be specified.",
                handler: CommandHandler.Create<IConsole>(GetProfiles),
                isHidden: false);

        // FIXME: Read from a config file!
        internal static IEnumerable<Profile> DotNETRuntimeProfiles { get; } = new[] {
            new Profile(
                "runtime-basic",
                new Provider[] {
                    new Provider("Microsoft-DotNETCore-SampleProfiler"),
                    new Provider("Microsoft-Windows-DotNETRuntime", (ulong)ClrTraceEventParser.Keywords.Default, EventLevel.Informational),
                },
                "Useful for tracking CPU usage and general runtime information. This the default option if no profile is specified."),
            new Profile(
                "gc",
                new Provider[] {
                    new Provider("Microsoft-DotNETCore-SampleProfiler"),
                    new Provider(
                        name: "Microsoft-Windows-DotNETRuntime",
                        keywords: (ulong)ClrTraceEventParser.Keywords.GC |
                                  (ulong)ClrTraceEventParser.Keywords.GCHeapSurvivalAndMovement |
                                  (ulong)ClrTraceEventParser.Keywords.Stack |
                                  (ulong)ClrTraceEventParser.Keywords.Jit |
                                  (ulong)ClrTraceEventParser.Keywords.StopEnumeration |
                                  (ulong)ClrTraceEventParser.Keywords.SupressNGen |
                                  (ulong)ClrTraceEventParser.Keywords.Loader |
                                  (ulong)ClrTraceEventParser.Keywords.Exception,
                        eventLevel: EventLevel.Verbose),
                },
                "Tracks allocation and collection performance."),
            new Profile(
                "gc-collect",
                new Provider[] {
                    new Provider("Microsoft-DotNETCore-SampleProfiler"),
                    new Provider(
                        name: "Microsoft-Windows-DotNETRuntime",
                        keywords:   (ulong)ClrTraceEventParser.Keywords.GC |
                                    (ulong)ClrTraceEventParser.Keywords.Exception,
                        eventLevel: EventLevel.Informational),
                },
                "Tracks GC collection only at very low overhead."),

            new Profile(
                "none",
                null,
                "Tracks nothing. Only providers specified by the --providers option will be available."),
        };
    }
}
