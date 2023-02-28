// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Internal.Common.Utils
{
    internal static class CommandUtils
    {
        // Returns processId that matches the given name.
        // It also checks whether the process has a diagnostics server port.
        // If there are more than 1 process with the given name or there isn't any active process
        // with the given name, then this returns -1
        public static int FindProcessIdWithName(string name)
        {
            var publishedProcessesPids = new List<int>(DiagnosticsClient.GetPublishedProcesses());
            var processesWithMatchingName = Process.GetProcessesByName(name);
            var commonId = -1;

            for (int i = 0; i < processesWithMatchingName.Length; i++)
            {
                if (publishedProcessesPids.Contains(processesWithMatchingName[i].Id))
                {
                    if (commonId != -1)
                    {
                        Console.WriteLine("There are more than one active processes with the given name: {0}", name);
                        return -1;
                    }
                    commonId = processesWithMatchingName[i].Id;
                }
            }
            if (commonId == -1)
            {
                Console.WriteLine("There is no active process with the given name: {0}", name);
            }
            return commonId;
        }

        /// <summary>
        /// A helper method for validating --process-id, --name, --diagnostic-port options for collect with child process commands.
        /// None of these options can be specified, so it checks for them and prints the appropriate error message.
        /// </summary>
        /// <param name="processId">process ID</param>
        /// <param name="name">name</param>
        /// <param name="port">port</param>
        /// <returns></returns>
        public static bool ValidateArgumentsForChildProcess(int processId, string name, string port)
        {
            if (processId != 0 && name != null && !string.IsNullOrEmpty(port))
            {
                Console.WriteLine("None of the --name, --process-id, or --diagnostic-port options may be specified when launching a child process.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// A helper method for validating --process-id, --name, --diagnostic-port options for collect commands.
        /// Only one of these options can be specified, so it checks for duplicate options specified and if there is
        /// such duplication, it prints the appropriate error message.
        /// </summary>
        /// <param name="processId">process ID</param>
        /// <param name="name">name</param>
        /// <param name="port">port</param>
        /// <param name="resolvedProcessId">resolvedProcessId</param>
        /// <returns></returns>
        public static bool ValidateArgumentsForAttach(int processId, string name, string port, out int resolvedProcessId)
        {
            resolvedProcessId = -1;
            if (processId == 0 && name == null && string.IsNullOrEmpty(port))
            {
                Console.WriteLine("Must specify either --process-id, --name, or --diagnostic-port.");
                return false;
            }
            else if (processId < 0)
            {
                Console.WriteLine($"{processId} is not a valid process ID");
                return false;
            }
            else if (processId != 0 && name != null && !string.IsNullOrEmpty(port))
            {
                Console.WriteLine("Only one of the --name, --process-id, or --diagnostic-port options may be specified.");
                return false;
            }
            else if (processId != 0 && name != null)
            {
                Console.WriteLine("Can only one of specify --name or --process-id.");
                return false;
            }
            else if (processId != 0 && !string.IsNullOrEmpty(port))
            {
                Console.WriteLine("Can only one of specify --process-id or --diagnostic-port.");
                return false;
            }
            else if (name != null && !string.IsNullOrEmpty(port))
            {
                Console.WriteLine("Can only one of specify --name or --diagnostic-port.");
                return false;
            }
            // If we got this far it means only one of --name/--diagnostic-port/--process-id was specified
            else if (!string.IsNullOrEmpty(port))
            {
                return true;
            }
            // Resolve name option
            else if (name != null)
            {
                processId = CommandUtils.FindProcessIdWithName(name);
                if (processId < 0)
                {
                    return false;
                }
            }
            else if (processId == 0)
            {
                Console.WriteLine("One of the --name, --process-id, or --diagnostic-port options must be specified when attaching to a process.");
                return false;
            }
            resolvedProcessId = processId;
            return true;
        }
    }

    internal class LineRewriter
    {
        public int LineToClear { get; set; } = 0;

        public LineRewriter() {}

        // ANSI escape codes:
        //  [2K => clear current line
        //  [{LineToClear};0H => move cursor to column 0 of row `LineToClear`
        public void RewriteConsoleLine()
        {
            var useConsoleFallback = true;
            if (!Console.IsInputRedirected)
            {
                // in case of console input redirection, the control ANSI codes would appear

                // first attempt ANSI Codes
                int before = Console.CursorTop;
                Console.Out.Write($"\u001b[2K\u001b[{LineToClear};0H");
                int after = Console.CursorTop;

                // Some consoles claim to be VT100 compliant, but don't respect
                // all of the ANSI codes, so fallback to the System.Console impl in that case
                useConsoleFallback = (before == after);
            }

            if (useConsoleFallback)
            {
                SystemConsoleLineRewriter();
            }
        }

        private void SystemConsoleLineRewriter() => Console.SetCursorPosition(0, LineToClear);
    }

    internal static class ReturnCode
    {
        public static int Ok = 0;
        public static int SessionCreationError = 1;
        public static int TracingError = 2;
        public static int ArgumentError = 3;
        public static int UnknownError = 4;
    }
}
