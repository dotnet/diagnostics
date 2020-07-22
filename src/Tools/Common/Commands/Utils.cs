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
}
