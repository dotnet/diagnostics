// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient.Eventing;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class PortsCommandHandler
    {
        public static async Task<int> GetActivePorts(IConsole console)
        {
            try
            {
                foreach (var pid in EventPipeClient.ListAvailablePorts())
                    Console.Out.WriteLine($"{System.Diagnostics.Process.GetProcessById(pid).ProcessName}({pid})");

                await Task.FromResult(0);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR]: {ex.ToString()}");
                return 1;
            }
        }

        public static Command ActivePortsCommand() =>
            new Command(
                name: "ports",
                description: "List all active DotNet Core Diagnostic ports.",
                handler: CommandHandler.Create<IConsole>(GetActivePorts),
                isHidden: true);
    }
}
