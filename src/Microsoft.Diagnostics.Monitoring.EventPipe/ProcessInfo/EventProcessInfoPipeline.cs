// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventProcessInfoPipeline : EventSourcePipeline<EventProcessInfoPipelineSettings>
    {
        private readonly Func<string, CancellationToken, Task> _onCommandLine;

        public EventProcessInfoPipeline(DiagnosticsClient client, EventProcessInfoPipelineSettings settings, Func<string, CancellationToken, Task> onCommandLine)
            : base(client, settings)
        {
            _onCommandLine = onCommandLine ?? throw new ArgumentNullException(nameof(onCommandLine));
        }

        protected override MonitoringSourceConfiguration CreateConfiguration()
        {
            return new SampleProfilerConfiguration();
        }

        protected override async Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            string commandLine = null;
            Action<TraceEvent, Action> processInfoHandler = (TraceEvent traceEvent, Action taskComplete) => {
                commandLine = (string)traceEvent.PayloadByName("CommandLine");
                taskComplete();
            };

            // Completed when the ProcessInfo event of the Microsoft-DotNETCore-EventPipe event provider is handled
            using EventTaskSource<Action<TraceEvent>> processInfoTaskSource = new(
                taskComplete => traceEvent => processInfoHandler(traceEvent, taskComplete),
                handler => eventSource.Dynamic.AddCallbackForProviderEvent(MonitoringSourceConfiguration.EventPipeProviderName, "ProcessInfo", handler),
                handler => eventSource.Dynamic.RemoveCallback(handler),
                token);

            // Completed when any trace event is handled
            using EventTaskSource<Action<TraceEvent>> anyEventTaskSource = new(
                taskComplete => traceEvent => taskComplete(),
                handler => eventSource.Dynamic.All += handler,
                handler => eventSource.Dynamic.All -= handler,
                token);

            // Wait for any trace event to be processed
            await anyEventTaskSource.Task.ConfigureAwait(false);

            // Stop the event pipe session
            await stopSessionAsync().ConfigureAwait(false);

            // Wait for the ProcessInfo event to be processed
            await processInfoTaskSource.Task.ConfigureAwait(false);

            // Notify of command line information
            await _onCommandLine(commandLine, token).ConfigureAwait(false);
        }
    }
}
