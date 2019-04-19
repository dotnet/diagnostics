// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
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
                Console.Out.WriteLine("Provider: Microsoft-Windows-DotNETRuntime");
                foreach ((var profile, var _, var description) in DotNETRuntimeProfiles)
                    Console.Out.WriteLine($"\t{profile, -16} - {description}");

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
        // TODO: Is the "string provider" expected to be "string[] providers"?
        private static IEnumerable<(string profile, Provider? provider, string description)> DotNETRuntimeProfiles { get; } = new (string profile, Provider? provider, string description)[] {
            ("runtime-basic", new Provider("Microsoft-Windows-DotNETRuntime", 0x00000004C14FCCBD, EventLevel.Informational), "Useful for tracking CPU usage and general runtime information. This the default option if no profile is specified."),
            ("gc", new Provider("Microsoft-Windows-DotNETRuntime", 0x0000000000000001, EventLevel.Verbose), "Tracks allocation and collection performance."),
            ("gc-collect", new Provider("Microsoft-Windows-DotNETRuntime", 0x0000000000800000, EventLevel.Informational), "Tracks GC collection only at very low overhead."),
            ("none", null, "Tracks nothing. Only providers specified by the --providers option will be available."),
        };
    }
}
