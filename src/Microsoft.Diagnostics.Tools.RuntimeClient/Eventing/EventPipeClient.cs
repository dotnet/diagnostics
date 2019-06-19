// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient.DiagnosticsIpc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    public static class EventPipeClient
    {
        private static string DiagnosticPortPattern { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"^dotnet-diagnostic-(\d+)$" : @"^dotnet-diagnostic-(\d+)-(\d+)-socket$";

        private static string IpcRootPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe\" : Path.GetTempPath();

        /// <summary>
        /// Get the files associated with the opened IPC Ports for DotNet Core applications.
        /// </summary>
        /// <returns>
        /// A collection of process identifiers associated with the list of opened files (IPC ports).
        /// These process Ids might have expired and not properly cleaned up.
        /// </returns>
        public static IEnumerable<int> ListAvailablePorts()
        {
            return Directory.GetFiles(IpcRootPath)
                .Select(namedPipe => (new FileInfo(namedPipe)).Name)
                .Where(input => Regex.IsMatch(input, DiagnosticPortPattern))
                .Select(input => int.Parse(Regex.Match(input, DiagnosticPortPattern).Groups[1].Value, NumberStyles.Integer));
        }

        /// <summary>
        /// Start trace collection.
        /// </summary>
        /// <param name="processId">Runtime process to trace</param>
        /// <param name="configuration">buffer size and provider configuration</param>
        /// <param name="sessionId">session id</param>
        /// <returns>Stream</returns>
        public static Stream CollectTracing(int processId, SessionConfiguration configuration, out ulong sessionId)
        {
            sessionId = 0;
            var message = new IpcMessage(DiagnosticServerCommandSet.EventPipe, (byte)EventPipeCommandId.CollectTracing, configuration.Serialize());
            var stream = IpcClient.SendMessage(processId, message, out var response);

            switch ((DiagnosticServerCommandId)response.Header.CommandId)
            {
                case DiagnosticServerCommandId.OK:
                    sessionId = BitConverter.ToUInt64(response.Payload);
                    break;
                case DiagnosticServerCommandId.Error:
                    // bad...
                    var hr = BitConverter.ToInt32(response.Payload);
                    throw new Exception($"Session start FAILED 0x{hr:X8}");
                default:
                    break;
            }

            return stream;
        }

        /// <summary>
        /// Turn off EventPipe logging session for the specified process Id.
        /// </summary>
        /// <param name="processId">Process Id to turn off logging session.</param>
        /// <param name="sessionId">EventPipe session Id to turn off.</param>
        /// <returns>It returns sessionId if success, otherwise 0.</returns>
        public static ulong StopTracing(int processId, ulong sessionId)
        {
            if (sessionId == 0)
                return sessionId; // TODO: Throw here instead?

            byte[] payload = BitConverter.GetBytes(sessionId);

            var response = IpcClient.SendMessage(processId, new IpcMessage(DiagnosticServerCommandSet.EventPipe, (byte)EventPipeCommandId.StopTracing, payload));

            switch ((DiagnosticServerCommandId)response.Header.CommandId)
            {
                case DiagnosticServerCommandId.OK:
                    return BitConverter.ToUInt64(response.Payload);
                case DiagnosticServerCommandId.Error:
                    return 0;
                default:
                    return 0;
            }
        }
    }
}
