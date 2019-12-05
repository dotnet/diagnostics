// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class MonitorCommandHandler
    {
        delegate Task<int> MonitorDelegate(CancellationToken ct, IConsole console, int processId, uint buffersize, string providers, string profile, TimeSpan duration);

        /// <summary>
        /// Prints a diagnostic trace from a currently running process in real-time.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the trace from.</param>
        /// <param name="buffersize">Sets the size of the in-memory circular buffer in megabytes.</param>
        /// <param name="providers">A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: 'KnownProviderName[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'</param>
        /// <param name="profile">A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.</param>
        /// <returns></returns>
        private static Task<int> Monitor(CancellationToken ct, IConsole console, int processId, uint buffersize, string providers, string profile, TimeSpan duration)
        {
            Action<TraceEvent> eventCallback =
                (traceEvent) =>
                {
                    Console.Out.WriteLine(traceEvent);
                };

            return CommandHelpers.Trace(ct, console, processId, buffersize, providers, profile, duration,
                onBeforeStart: () => { },
                onStart: (info) =>
                {
                    using (var pipeEventSource = new EventPipeEventSource(info.EventPipeStream))
                    {
                        var eventParser = new ClrTraceEventParser(pipeEventSource);
                        eventParser.All += eventCallback;

                        try
                        {
                            pipeEventSource.Process();
                        }
                        finally
                        {
                            eventParser.All -= eventCallback;
                        }
                    }
                },
                onSuccess: () => { });
        }

        public static Command MonitorCommand() =>
            new Command(
                name: "monitor",
                description: "Prints a diagnostic trace from a currently running process in real-time",
                symbols: new Option[] {
                    CommonOptions.ProcessIdOption(),
                    CommonOptions.CircularBufferOption(),
                    CommonOptions.ProvidersOption(),
                    CommonOptions.ProfileOption(),
                    CommonOptions.DurationOption()
                },
                handler: HandlerDescriptor.FromDelegate((MonitorDelegate)Monitor).GetCommandHandler());
    }
}
