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
    internal static class ProvidersCommandHandler
    {
        public static async Task<int> KnownProviders(IConsole console)
        {
            try
            {
                foreach (var provider in CommonProviders)
                {
                    var filterData = provider.FilterData == null ? "" : $":{provider.FilterData}";
                    Console.Out.WriteLine($"Provider={provider.Name}:0x{provider.Keywords:X16}:{(uint)provider.EventLevel}{filterData}");
                }

                await Task.FromResult(0);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return 1;
            }
        }

        public static Command KnownProvidersCommand() =>
            new Command(
                name: "knownproviders",
                description: "List known tracing flags.",
                handler: CommandHandler.Create<IConsole>(KnownProviders),
                isHidden: true);

        private static IEnumerable<Provider> CommonProviders { get; } = new[] {
            new Provider("Microsoft-Windows-DotNETRuntime", 0x0000000000000001, EventLevel.Informational),  // ClrGC
            new Provider("Microsoft-Windows-DotNETRuntime", 0x0000000000000002, EventLevel.Informational),  // ClrThreadPool
            new Provider("Microsoft-Windows-DotNETRuntime", 0x00000000FFFFFFFF, EventLevel.Verbose),        // ClrAll
            new Provider("Microsoft-Windows-DotNETRuntime", 0x00000004C14FCCBD, EventLevel.Informational),  //
            new Provider("Microsoft-Windows-DotNETRuntimeRundown"),
        };
    }
}
