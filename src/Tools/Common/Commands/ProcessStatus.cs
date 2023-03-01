// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;
using Process = System.Diagnostics.Process;

namespace Microsoft.Internal.Common.Commands
{
    public class ProcessStatusCommandHandler
    {
        public static Command ProcessStatusCommand(string description) =>
            new Command(name: "ps", description)
            {
                HandlerDescriptor.FromDelegate((ProcessStatusDelegate)ProcessStatus).GetCommandHandler()
            };

        private delegate void ProcessStatusDelegate(IConsole console);

        private static void MakeFixedWidth(string text, int width, StringBuilder sb, bool leftPad = false, bool truncateFront = false)
        {
            int textLength = text.Length;
            sb.Append(' ');
            if (textLength == width)
            {
                sb.Append(text);
            }
            else if (textLength > width)
            {
                if (truncateFront)
                {
                    sb.Append(text.Substring(textLength - width, width));
                }
                else
                {
                    sb.Append(text.Substring(0, width));
                }
            }
            else
            {
                if (leftPad)
                {
                    sb.Append(' ', width - textLength);
                    sb.Append(text);
                }
                else
                {
                    sb.Append(text);
                    sb.Append(' ', width - text.Length);
                }

            }
            sb.Append(' ');
        }

        private struct ProcessDetails
        {
            public int ProcessId;
            public string ProcessName;
            public string FileName;
            public string CmdLineArgs;
        }

        /// <summary>
        /// Print the current list of available .NET core processes for diagnosis, their statuses and the command line arguments that are passed to them.
        /// </summary>
        public static void ProcessStatus(IConsole console)
        {
            int GetColumnWidth(IEnumerable<int> fieldWidths)
            {
                int consoleWidth = 0;
                if (Console.IsOutputRedirected)
                {
                    consoleWidth = int.MaxValue;
                }
                else
                {
                    consoleWidth = Console.WindowWidth;
                }
                int extra = (int)Math.Ceiling(consoleWidth * 0.05);
                int largeLength = consoleWidth / 2 - 16 - extra;
                return Math.Min(fieldWidths.Max(), largeLength);
            }

            void FormatTableRows(List<ProcessDetails> rows, StringBuilder tableText)
            {
                if (rows.Count == 0)
                {
                    tableText.Append("No supported .NET processes were found");
                    return;
                }
                var processIDs = rows.Select(i => i.ProcessId.ToString().Length);
                var processNames = rows.Select(i => i.ProcessName.Length);
                var fileNames = rows.Select(i => i.FileName.Length);
                var commandLineArgs = rows.Select(i => i.CmdLineArgs.Length);
                int iDLength = GetColumnWidth(processIDs);
                int nameLength = GetColumnWidth(processNames);
                int fileLength = GetColumnWidth(fileNames);
                int cmdLength = GetColumnWidth(commandLineArgs);

                foreach (var info in rows)
                {
                    MakeFixedWidth(info.ProcessId.ToString(), iDLength, tableText, true, true);
                    MakeFixedWidth(info.ProcessName, nameLength, tableText, false, true);
                    MakeFixedWidth(info.FileName, fileLength, tableText, false, true);
                    MakeFixedWidth(info.CmdLineArgs, cmdLength, tableText, false, true);
                    tableText.Append('\n');
                }
            }
            try
            {
                StringBuilder sb = new StringBuilder();
                var processes = DiagnosticsClient.GetPublishedProcesses()
                    .Select(GetProcessById)
                    .Where(process => process != null)
                    .OrderBy(process => process.ProcessName)
                    .ThenBy(process => process.Id);

                var currentPid = Process.GetCurrentProcess().Id;
                List<ProcessDetails> printInfo = new ();
                foreach (var process in processes)
                {
                    if (process.Id == currentPid)
                    {
                        continue;
                    }
                    try
                    {
                        string cmdLineArgs = GetArgs(process);
                        cmdLineArgs = cmdLineArgs == process.MainModule?.FileName ? string.Empty : cmdLineArgs;
                        string fileName = process.MainModule?.FileName ?? string.Empty;
                        var commandInfo = new ProcessDetails()
                        {
                            ProcessId = process.Id,
                            ProcessName = process.ProcessName,
                            FileName = fileName,
                            CmdLineArgs = cmdLineArgs
                        };
                        printInfo.Add(commandInfo);
                    }
                    catch (Exception ex)
                    {
                        if (ex is Win32Exception or InvalidOperationException)
                        {
                            var commandInfo = new ProcessDetails()
                            {
                                ProcessId = process.Id,
                                ProcessName = process.ProcessName,
                                FileName = "[Elevated process - cannot determine path]",
                                CmdLineArgs = ""
                            };
                            printInfo.Add(commandInfo);
                        }
                        else
                        {
                            Debug.WriteLine($"[PrintProcessStatus] {ex.ToString()}");
                        }
                    }
                }
                FormatTableRows(printInfo, sb);
                console.Out.WriteLine(sb.ToString());
            }
            catch (Exception ex)
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
                    string commandLine = WindowsProcessExtension.GetCommandLine(process);
                    if (!string.IsNullOrWhiteSpace(commandLine))
                    {
                        string[] commandLineSplit = commandLine.Split(' ');
                        if (commandLineSplit.FirstOrDefault() == process.ProcessName)
                        {
                            return string.Join(" ", commandLineSplit.Skip(1));
                        }
                        return commandLine;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    return "[Elevated process - cannot determine command line arguments]";
                }

            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    string commandLine = File.ReadAllText($"/proc/{process.Id}/cmdline");
                    if (!string.IsNullOrWhiteSpace(commandLine))
                    {
                        //The command line may be modified and the first part of the command line may not be /path/to/exe. If that is the case, return the command line as is.Else remove the path to module as we are already displaying that.
                        string[] commandLineSplit = commandLine.Split('\0');
                        if (commandLineSplit.FirstOrDefault() == process.MainModule?.FileName)
                        {
                            return string.Join(" ", commandLineSplit.Skip(1));
                        }
                        return commandLine.Replace("\0", " ");
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
