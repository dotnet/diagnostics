// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Process = System.Diagnostics.Process;

namespace Microsoft.Internal.Common.Commands
{
    public class ProcessStatusCommandHandler
    {
        public static Command ProcessStatusCommand(string description) =>
            new Command(name: "ps", description)
            {
                Handler = CommandHandler.Create<IConsole>(PrintProcessStatus)
            };

        /// <summary>
        /// Print the current list of available .NET core processes for diagnosis and their statuses
        /// </summary>
        public static void PrintProcessStatus(IConsole console)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                var processes = DiagnosticsClient.GetPublishedProcesses()
                    .Select(GetProcessById)
                    .Where(process => process != null)
                    .OrderBy(process => process.ProcessName)
                    .ThenBy(process => process.Id);

                var currentPid = Process.GetCurrentProcess().Id;

                foreach (var process in processes)
                {
                    if (process.Id == currentPid)
                    {
                        continue;
                    }

                    try
                    {
                        sb.Append($"{process.Id,10} {process.ProcessName,-10} {process.MainModule.FileName}\n");
                    }
                    catch (Exception ex)
                    {
                        if (ex is System.ComponentModel.Win32Exception || ex is NullReferenceException)
                        {
                            sb.Append($"{process.Id,10} {process.ProcessName,-10} [Elevated process - cannot determine path]\n");
                        }
                        else
                        {
                            Debug.WriteLine($"[PrintProcessStatus] {ex.ToString()}");
                        }
                    }
                }
                console.Out.WriteLine(sb.ToString());
            }
            catch (InvalidOperationException ex)
            {
                console.Out.WriteLine(ex.ToString());
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
    }
}
