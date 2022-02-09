// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Tools.Common;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Process = System.Diagnostics.Process;
using System.IO;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tools.Trace.CommandLine;
using System.CommandLine.Binding;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Internal.Common.Commands
{        
    public class ProcessStatusCommandHandler
    {
        public static Command ProcessStatusCommand() =>
            new Command(name: "ps", 
            description: "Lists the dotnet processes that traces can be collected")
            {
                HandlerDescriptor.FromDelegate((ProcessStatusDelegate)ProcessStatus).GetCommandHandler()
            };

        delegate void ProcessStatusDelegate(IConsole console);
        static void MakeFixedWidth(string text, int width, StringBuilder sb, bool leftPad = false, bool truncateFront = false)
        {
            int textLength = text.Length;
            sb.Append(" ");
            if(textLength == width)
            {
                sb.Append(text);
            }
            else if(textLength > width)
            {
                if(truncateFront)
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
            sb.Append(" ");
        }
        struct ProcessDetails
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
                int largeLength = Console.WindowWidth / 2 - 16;
                return Math.Min(fieldWidths.Max(), largeLength);
            }

            void FormatTableRows(List<ProcessDetails> rows, StringBuilder tableText)
            {
                var processIDs = rows.Select(i => i.ProcessId.ToString().Length);
                var processNames = rows.Select(i => i.ProcessName.Length);
                var fileNames = rows.Select(i => i.FileName.Length);
                var commandLineArgs = rows.Select(i => i.CmdLineArgs.Length);
                int iDLength = GetColumnWidth(processIDs);
                int nameLength = GetColumnWidth(processNames);
                int fileLength = GetColumnWidth(fileNames);
                int cmdLength = GetColumnWidth(commandLineArgs);

                foreach(var info in rows)
                {
                    MakeFixedWidth(info.ProcessId.ToString(), iDLength, tableText, true, true);
                    MakeFixedWidth(info.ProcessName, nameLength, tableText, false, true);
                    MakeFixedWidth(info.FileName, fileLength, tableText, false, true);
                    MakeFixedWidth(info.CmdLineArgs, cmdLength, tableText, false, true);
                    tableText.Append("\n");
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
                List<Microsoft.Internal.Common.Commands.ProcessStatusCommandHandler.ProcessDetails> printInfo = new ();
                foreach (var process in processes)
                {
                    if (process.Id == currentPid)
                    {
                        continue;
                    }
                    try
                    {
                        String cmdLineArgs = GetArgs(process);
                        cmdLineArgs = cmdLineArgs == process.MainModule?.FileName ? string.Empty : cmdLineArgs;
                        string fileName = process.MainModule?.FileName ?? string.Empty;
                        string[] cmdList = cmdLineArgs.Split(" ");
                        string separator = "";
                        foreach(string str in cmdList)
                        {
                            if (str == string.Empty)
                            {
                                break;
                            }
                            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            {
                                separator = "\\";
                            }
                            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                            {
                                separator = "/";
                            }
                            if (str.Contains(separator))
                            {
                                //Assume the first string to contain the directory separation character is the filepath
                                fileName = str;
                                //remove the filepath from the command line arguments
                                cmdLineArgs = string.Join(" ", cmdList.Skip(1));
                            }
                            break;
                            
                        }
                       // ProcessDetails commandInfo = ProcessDetails {process.Id, process.ProcessName, fileName, cmdLineArgs};
                        var commandInfo = new ProcessDetails();
                        commandInfo.ProcessId = process.Id;
                        commandInfo.ProcessName = process.ProcessName;
                        commandInfo.FileName = fileName;
                        commandInfo.CmdLineArgs = cmdLineArgs;
                        printInfo.Add(commandInfo);
                    }
                    catch (Exception ex) 
                    {
                        if (ex is Win32Exception || ex is InvalidOperationException)
                        {
                            var commandInfo = new ProcessDetails();
                            commandInfo.ProcessId = process.Id;
                            commandInfo.ProcessName = process.ProcessName;
                            commandInfo.FileName = "[Elevated process - cannot determine path]";
                            commandInfo.CmdLineArgs = "";
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
                    string commandLine = WindowsProcessExtension.GetCommandLine(process);
                    if (!String.IsNullOrWhiteSpace(commandLine))
                    {
                        string[] commandLineSplit = commandLine.Split(' ');
                        if (commandLineSplit.FirstOrDefault() == process.ProcessName)
                        {
                            return String.Join(" ", commandLineSplit.Skip(1));
                        }
                        return commandLine;
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
                        if (commandLineSplit.FirstOrDefault() == process.MainModule?.FileName)
                        {
                            return String.Join(" ", commandLineSplit.Skip(1));
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
