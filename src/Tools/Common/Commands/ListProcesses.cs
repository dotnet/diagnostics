// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Microsoft.Diagnostics.Tools.RuntimeClient;

namespace Microsoft.Internal.Common.Commands
{
    public class ProcessStatusCommandHandler
    {
        /// <summary>
        /// Get all active process ports
        /// </summary>
        /// <returns>
        /// A collection of string identifiers that show the current processes
        /// </returns>
        public static void PrintProcessStatus(IConsole console)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                var processes = EventPipeClient.ListAvailablePorts()
                    .Select(GetProcessById)
                    .Where(process => process != null)
                    .OrderBy(process => process.ProcessName)
                    .ThenBy(process => process.Id);

                foreach (var process in processes)
                {
                    try
                    {
                        sb.Append($"{process.Id, 10} {process.ProcessName, -10} {process.MainModule.FileName}\n");
                    }
                    catch (Exception)
                    {
                        sb.Append($"{process.Id, 10} {process.ProcessName, -10} [Elevated process - cannot determine path]\n");
                    }
                }
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
    }
}
