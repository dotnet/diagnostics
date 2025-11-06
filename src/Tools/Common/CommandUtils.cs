// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tools;

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
            List<int> publishedProcessesPids = new(DiagnosticsClient.GetPublishedProcesses());
            Process[] processesWithMatchingName = Process.GetProcessesByName(name);
            int commonId = -1;

            for (int i = 0; i < processesWithMatchingName.Length; i++)
            {
                if (publishedProcessesPids.Contains(processesWithMatchingName[i].Id))
                {
                    if (commonId != -1)
                    {
                        throw new DiagnosticToolException($"There are more than one active processes with the given name: {name}");
                    }
                    commonId = processesWithMatchingName[i].Id;
                }
            }
            if (commonId == -1)
            {
                throw new DiagnosticToolException($"There is no active process with the given name: {name}");
            }
            return commonId;
        }

        // <summary>
        // Returns processId that matches the given dsrouter.
        // </summary>
        // <param name="dsrouter">dsrouterCommand</param>
        // <returns>processId</returns>
        public static int LaunchDSRouterProcess(string dsrouterCommand)
        {
            Console.WriteLine("For finer control over the dotnet-dsrouter options, run it separately and connect to it using -p" + Environment.NewLine);

            return DsRouterProcessLauncher.Launcher.Start(dsrouterCommand, default);
        }


        /// <summary>
        /// A helper method for validating --process-id, --name, --diagnostic-port options for collect with child process commands.
        /// None of these options can be specified, so it checks for them and prints the appropriate error message.
        /// </summary>
        /// <param name="processId">process ID</param>
        /// <param name="name">name</param>
        /// <param name="port">port</param>
        /// <returns></returns>
        public static void ValidateArgumentsForChildProcess(int processId, string name, string port)
        {
            if (processId != 0 || name != null || !string.IsNullOrEmpty(port))
            {
                throw new DiagnosticToolException("None of the --name, --process-id, or --diagnostic-port options may be specified when launching a child process.");
            }
        }

        /// <summary>
        /// A helper method for validating --process-id, --name options for collect commands and resolving the process ID and name.
        /// Only one of these options can be specified, so it checks for duplicate options specified and if there is
        /// such duplication, it throws the appropriate DiagnosticToolException error message.
        /// </summary>
        /// <param name="processId">process ID</param>
        /// <param name="name">name</param>
        /// <param name="resolvedProcessId">resolvedProcessId</param>
        /// <param name="resolvedProcessName">resolvedProcessName</param>
        /// <returns></returns>
        public static void ResolveProcess(int processId, string name, out int resolvedProcessId, out string resolvedProcessName)
        {
            resolvedProcessId = -1;
            resolvedProcessName = name;
            if (processId == 0 && string.IsNullOrEmpty(name))
            {
                throw new DiagnosticToolException("Must specify either --process-id or --name.");
            }
            else if (processId < 0)
            {
                throw new DiagnosticToolException($"{processId} is not a valid process ID");
            }
            else if ((processId != 0) && !string.IsNullOrEmpty(name))
            {
                throw new DiagnosticToolException("Only one of the --name or --process-id options may be specified.");
            }
            try
            {
                if (processId != 0)
                {
                    Process process = Process.GetProcessById(processId);
                    resolvedProcessId = processId;
                    resolvedProcessName = process.ProcessName;
                }
                else
                {
                    resolvedProcessId = FindProcessIdWithName(name);
                }
            }
            catch (ArgumentException)
            {
                throw new DiagnosticToolException($"No process with ID {processId} is currently running.");
            }
        }

        /// <summary>
        /// A helper method for validating --process-id, --name, --diagnostic-port, --dsrouter options for collect commands and resolving the process ID.
        /// Only one of these options can be specified, so it checks for duplicate options specified and if there is
        /// such duplication, it throws the appropriate DiagnosticToolException error message.
        /// </summary>
        /// <param name="processId">process ID</param>
        /// <param name="name">name</param>
        /// <param name="port">port</param>
        /// <param name="dsrouter">dsrouter</param>
        /// <param name="resolvedProcessId">resolvedProcessId</param>
        /// <returns></returns>
        public static void ResolveProcessForAttach(int processId, string name, string port, string dsrouter, out int resolvedProcessId)
        {
            resolvedProcessId = -1;
            if (processId == 0 && string.IsNullOrEmpty(name) && string.IsNullOrEmpty(port) && string.IsNullOrEmpty(dsrouter))
            {
                throw new DiagnosticToolException("Must specify either --process-id, --name, --diagnostic-port, or --dsrouter.");
            }
            else if (processId < 0)
            {
                throw new DiagnosticToolException($"{processId} is not a valid process ID");
            }
            else if ((processId != 0 ? 1 : 0) +
                     (!string.IsNullOrEmpty(name) ? 1 : 0) +
                     (!string.IsNullOrEmpty(port) ? 1 : 0) +
                     (!string.IsNullOrEmpty(dsrouter) ? 1 : 0)
                     != 1)
            {
                throw new DiagnosticToolException("Only one of the --name, --process-id, --diagnostic-port, or --dsrouter options may be specified.");
            }
            // If we got this far it means only one of --name/--diagnostic-port/--process-id/--dsrouter was specified
            else if (!string.IsNullOrEmpty(port))
            {
                return;
            }
            // Resolve name option
            else if (!string.IsNullOrEmpty(name))
            {
                processId = FindProcessIdWithName(name);
            }
            else if (!string.IsNullOrEmpty(dsrouter))
            {
                if (dsrouter != "ios" && dsrouter != "android" && dsrouter != "ios-sim" && dsrouter != "android-emu")
                {
                    throw new DiagnosticToolException("Invalid value for --dsrouter. Valid values are 'ios', 'ios-sim', 'android' and 'android-emu'.");
                }
                if ((processId = LaunchDSRouterProcess(dsrouter)) < 0)
                {
                    if (processId == -2)
                    {
                        throw new DiagnosticToolException($"Failed to launch dsrouter: {dsrouter}. Make sure that dotnet-dsrouter is not already running. You can connect to an already running dsrouter with -p.", ReturnCode.TracingError);
                    }
                    else
                    {
                        throw new DiagnosticToolException($"Failed to launch dsrouter: {dsrouter}. Please make sure that dotnet-dsrouter is installed and available in the same directory as dotnet-trace.\n" +
                                                             "You can install dotnet-dsrouter by running 'dotnet tool install --global dotnet-dsrouter'. More info at https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dsrouter", ReturnCode.TracingError);
                    }
                }
            }
            resolvedProcessId = processId;
        }
    }
}
