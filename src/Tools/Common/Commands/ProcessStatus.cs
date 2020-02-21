// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Process = System.Diagnostics.Process;

namespace Microsoft.Internal.Common.Commands
{
    public class ProcessStatusCommandHandler
    {
        public static Command ProcessStatusCommand(string description)
        {
            Command cmd = new Command(description);
            cmd.Handler = CommandHandler.Create<IConsole, string>(PrintProcessStatus);
            cmd.AddOption(FilterUserOption());
            return cmd;
        }

        private static Option FilterUserOption() =>
            new Option(
                aliases: new[] { "-u", "--user" },
                description: "Shows only process owned by a specific user")
            {
                Argument = new Argument<string>(name: "userName", defaultValue: "all")
            };

        /// <summary>
        /// Print the current list of available .NET core processes for diagnosis and their statuses
        /// </summary>
        public static void PrintProcessStatus(IConsole console, string userName = "")
        {
            int uid = -1;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (userName != "all" || userName != ""))
            {
                uid = MapUserNameToId(userName);
                if (uid == -1)
                {
                    console.Error.WriteLine("User name not found. Cannot filter. Showing all dotnet processes");
                }
            }

            else 
            {
                uid = -1;
            }

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
                        if(uid!=-1)
                        {
                            if(uid == GetProcessUser(process.Id))
                            {
                                sb.Append($"{process.Id,10} {process.ProcessName,-10} {process.MainModule.FileName}\n");
                            }
                        }
                        else
                        {
                            sb.Append($"{process.Id,10} {process.ProcessName,-10} {process.MainModule.FileName}\n");
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

        private static int GetProcessUser(int processId)
        {
            try
            {
                IEnumerable<string> fields = File.ReadLines($"/proc/{processId}/status");

                foreach(string field in fields)
                {
                    if (field.Contains("Uid"))
                    {
                        return int.Parse(field.Split(" ")[1]);
                    }
                }

                return -1;
            }

            catch (IOException)
            {
                return -1;
            }
        }

        private static int MapUserNameToId(string userName)
        {
            try
            {
                IEnumerable<string> users = File.ReadLines("/etc/passwd");
                foreach (string user in users)
                {
                    string[] fields = user.Split(":");
                    if (fields[0] == userName)
                    {
                        return int.Parse(fields[2]);
                    }
                }

                return -1;
            }

            catch (IOException)
            {
                return -1;
            }
        }
    }
}
