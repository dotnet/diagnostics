// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Internal.Common.Utils
{
    internal class CommandUtils
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
            // first attempt ANSI Codes
            int before = Console.CursorTop;
            Console.Out.Write($"\u001b[2K\u001b[{LineToClear};0H");
            int after = Console.CursorTop;
            // Some consoles claim to be VT100 compliant, but don't respect
            // all of the ANSI codes, so fallback to the System.Console impl in that case
            if (before == after)
                SystemConsoleLineRewriter();
        }

        private void SystemConsoleLineRewriter() => Console.SetCursorPosition(0, LineToClear);
    }
}
