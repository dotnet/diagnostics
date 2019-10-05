// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Internal.Common.Commands;
using System;
using System.CommandLine;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class ListProcessesCommandHandler
    {
        public static async Task<int> GetActivePorts(IConsole console)
        {
            ProcessStatusCommandHandler.PrintProcessStatus(console);
            await Task.FromResult(0);
            return 0;
        }

        public static Command ListProcessesCommand() =>
            new Command(
                name: "ps",
                description: "Lists dotnet processes that can be attached to.",
                handler: System.CommandLine.Invocation.CommandHandler.Create<IConsole>(GetActivePorts),
                isHidden: false);
    }
}
