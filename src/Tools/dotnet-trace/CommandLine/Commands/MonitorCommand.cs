// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tools.Trace.CommandLine.Options;
using Microsoft.Diagnostics.Tools.Trace.DiagnosticProfileHandlers;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Rendering;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;


namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class MonitorCommandHandler
    {
        delegate Task<int> MonitorDelegate(
            CancellationToken ct,
            IConsole console,
            // options
            int processId,
            string name,
            uint buffersize,
            // diagnostic profiles
            string gcPause,
            string http,
            string loaderBinder
        );

        /// <summary>
        /// Starts a trace and displays the trace's payload live from a process.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the trace from.</param>
        /// <param name="name">The name of process to collect the trace from.</param>
        /// <param name="output">The output path for the collected trace data.</param>
        /// <param name="buffersize">Sets the size of the in-memory circular buffer in megabytes.</param>
        /// <param name="providers">A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: 'KnownProviderName[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'</param>
        /// <param name="profile">A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.</param>
        /// <param name="format">The desired format of the created trace file.</param>
        /// <param name="duration">The duration of trace to be taken. </param>
        private static async Task<int> Monitor(
            CancellationToken ct,
            IConsole console,
            // trace specific options
            int processId,
            string name,
            uint buffersize,
            // diagnostic profiles
            string gcPause,
            string http,
            string loaderBinder
        )
        {
            //Debug.Assert(processId > 0 || name != null);
            Console.WriteLine("Hello");
            Console.WriteLine($"Received --gc-pause option: {gcPause}");

            await Task.Delay(100);
            return 1;
        }

        /// <summary>
        /// Generates a List of appropriate IDiagnosticProfileHandlers with the given options from users.
        /// This may throw if the user specified invalid options for any of the diagnostic profile they specified.
        /// </summary>
        /// <param name="gcPause">Option for --gc-pause</param>
        /// <param name="http">Option for --http</param>
        /// <param name="loaderBinder">Option for --loader-binder</param>
        /// <returns></returns>
        private static List<IDiagnosticProfileHandler> GetAllHandlers(string gcPause, string http, string loaderBinder)
        {
            List<IDiagnosticProfileHandler> handlers = new List<IDiagnosticProfileHandler>();
            if (gcPause != null)
            {
                handlers.Add(new GcPauseHandler(gcPause));
            }
            // TODO: add http, loader-binder here.
        }

        public static Command MonitorCommand() =>
             new Command(
                name: "monitor",
                description: "Collects a diagnostic trace from a currently running process")
             {
                 // Handler
                 HandlerDescriptor.FromDelegate((MonitorDelegate)Monitor).GetCommandHandler(),
                 // Options for trace session configuration
                 CommonOptions.ProcessIdOption(),
                 CommonOptions.CircularBufferOption(),
                 CommonOptions.NameOption(),
                 // Options for diagnostic profiles
                 DiagnosticProfiles.GCPauseProfileOption(),
                 DiagnosticProfiles.HttpProfileOption(),
                 DiagnosticProfiles.LoaderBinderProfileOption()
             };


    }
}