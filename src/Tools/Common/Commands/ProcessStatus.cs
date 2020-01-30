// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Management;
using Process = System.Diagnostics.Process;
using System.IO;

namespace Microsoft.Internal.Common.Commands
{
    public class ProcessStatusCommandHandler
    {
        public static Command ProcessStatusCommand(string description)
        {
            var cmd = new Command(name: "ps", description)
            {
                Handler = CommandHandler.Create<IConsole, bool>(PrintProcessStatus),
                

            };
            cmd.AddOption(CommandLineOption());
            return cmd;
        }

        private static Option CommandLineOption() =>
            new Option(
                aliases: new[] { "-f", "--full", "--cmd-line" },
                description: "Gets the commandline argument")
            {
                Argument = new Argument<bool>(name: "cmdline")
            };
        /// <summary>
        /// Print the current list of available .NET core processes for diagnosis and their statuses
        /// </summary>
        public static void PrintProcessStatus(IConsole console, bool cmdline)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                var processes = DiagnosticsClient.GetPublishedProcesses()
                    .Select(GetProcessById)
                    .Where(process => process != null)
                    .OrderBy(process => process.ProcessName)
                    .ThenBy(process => process.Id);

                foreach (var process in processes)
                {
                    try
                    {
                        
                        if (cmdline)
                        {
                            var cmdLineArgs = GetArgs(process);
                            cmdLineArgs = cmdLineArgs == process.MainModule.FileName?"": cmdLineArgs;
                            sb.Append($"{process.Id, 10} {process.ProcessName, -10} {process.MainModule.FileName, -10} {cmdLineArgs, -10}\n");
                        }
                        
                        else
                        {
                            sb.Append($"{process.Id, 10} {process.ProcessName, -10} {process.MainModule.FileName}\n");
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        sb.Append($"{process.Id, 10} {process.ProcessName, -10} [Elevated process - cannot determine path]\n");
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

        private static string GetArgs(Process process)
        {
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher($"SELECT CommandLine from Win32_Process WHERE ProcessId = {process.Id}"))
                {
                    using (ManagementObjectCollection objectCollection = searcher.Get())
                    {
                        return objectCollection.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString().Split("  ")?.Last().Replace("\"", "");
                    }
                }

            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    var cmdArgs = File.ReadAllText($"/proc/{process.Id}/cmdline")?.Split("\0").Skip(1).ToArray();
                    return String.Join("", cmdArgs).Replace("\0", "");
                }
                catch (IOException)
                {
                    return "[cannot determine command line arguments]";
                }
            }
            return null;
        }
    }
}
