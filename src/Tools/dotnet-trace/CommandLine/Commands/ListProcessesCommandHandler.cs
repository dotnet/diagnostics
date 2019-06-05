// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.CommandLine;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class ListProcessesCommandHandler
    {
        public static async Task<int> GetActivePorts(IConsole console)
        {
            try
            {
                var processes = EventPipeClient.ListAvailablePorts()
                    .Select(GetProcessById)
                    .Where(process => process != null)
                    .OrderBy(process => process.ProcessName)
                    .ThenBy(process => process.Id);

                foreach (var process in processes)
                {
                    try
                    {
                        Console.Out.WriteLine($"{process.Id, 10} {process.ProcessName, -10} {process.MainModule.FileName}");
                    }
                    catch (Exception)
                    {
                        Console.Out.WriteLine($"{process.Id, 10} {process.ProcessName, -10}");
                    }
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

        private static Process GetProcessById(int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static Command ListProcessesCommand() =>
            new Command(
                name: "list-processes",
                description: "Lists dotnet processes that can be attached to.",
                handler: System.CommandLine.Invocation.CommandHandler.Create<IConsole>(GetActivePorts),
                isHidden: false);
    }
}
