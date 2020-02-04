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
using System.ComponentModel;

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
        /// Print the current list of available .NET core processes for diagnosis, their statuses and the command line arguments that are passed to them.
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

                foreach (var process in processes)
                {
                    try
                    {
                        String cmdLineArgs = GetArgs(process);
                        cmdLineArgs = cmdLineArgs == process.MainModule.FileName ? "" : cmdLineArgs;
                        sb.Append($"{process.Id, 10} {process.ProcessName, -10} {process.MainModule.FileName, -10} {cmdLineArgs, -10}\n");

                    }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
                    {
                        sb.Append($"{process.Id, 10} {process.ProcessName, -10} [Elevated process - cannot determine path] [Elevated process - cannot determine commandline arguments]\n");
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher($"SELECT CommandLine from Win32_Process WHERE ProcessId = {process.Id}"))
                    {
                        using (ManagementObjectCollection objectCollection = searcher.Get())
                        {
                            return objectCollection.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString().Split("  ")?.Last().Replace("\"", "") ?? "";
                        }
                    }
                }
                catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
                {
                    return "[Elevated process - cannot determine command line arguments]";
                }

            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    string commandLine = File.ReadAllText($"/proc/{process.Id}/cmdline");
                    if(!String.IsNullOrWhiteSpace(commandLine))
                    {
                        //The command line may be modified and the first part of the command line may not be /path/to/exe. If that is the case, return the command line as is.Else remove the path to module as we are already displaying that.
                        string[] commandLineSplit = commandLine.Split('\0');
                        if (commandLineSplit.FirstOrDefault() == process.MainModule.FileName)
                        {
                            return String.Join("", commandLineSplit.Skip(1));
                        }
                        return commandLine;
                    }
                    return "";
                }
                catch (IOException)
                {
                    return "[cannot determine command line arguments]";
                }
            }
            return "";
        }
    }
}
