// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Graphs;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventGCDumpPipeline : EventSourcePipeline<EventGCPipelineSettings>
    {
        private readonly MemoryGraph _gcGraph;

        public EventGCDumpPipeline(DiagnosticsClient client, EventGCPipelineSettings settings, MemoryGraph gcGraph) : base(client, settings)
        {
            _gcGraph = gcGraph ?? throw new ArgumentNullException(nameof(gcGraph));
        }

        protected override MonitoringSourceConfiguration CreateConfiguration()
        {
            return new GCDumpSourceConfiguration();
        }

        protected override async Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            int gcNum = -1;

            Action<GCStartTraceData, Action> gcStartHandler = (GCStartTraceData data, Action taskComplete) =>
            {
                taskComplete();

                if (gcNum < 0 && data.Depth == 2 && data.Type != GCType.BackgroundGC)
                {
                    gcNum = data.Count;
                }
            };

            Action<GCBulkNodeTraceData, Action> gcBulkNodeHandler = (GCBulkNodeTraceData data, Action taskComplete) =>
            {
                taskComplete();
            };

            Action<GCEndTraceData, Action> gcEndHandler = (GCEndTraceData data, Action taskComplete) =>
            {
                if (data.Count == gcNum)
                {
                    taskComplete();
                }
            };

            // Register event handlers on the event source and represent their completion as tasks
            using var gcStartTaskSource = new EventTaskSource<Action<GCStartTraceData>>(
                taskComplete => data => gcStartHandler(data, taskComplete),
                handler => eventSource.Clr.GCStart += handler,
                handler => eventSource.Clr.GCStart -= handler,
                token);
            using var gcBulkNodeTaskSource = new EventTaskSource<Action<GCBulkNodeTraceData>>(
                taskComplete => data => gcBulkNodeHandler(data, taskComplete),
                handler => eventSource.Clr.GCBulkNode += handler,
                handler => eventSource.Clr.GCBulkNode -= handler,
                token);
            using var gcStopTaskSource = new EventTaskSource<Action<GCEndTraceData>>(
                taskComplete => data => gcEndHandler(data, taskComplete),
                handler => eventSource.Clr.GCStop += handler,
                handler => eventSource.Clr.GCStop -= handler,
                token);
            using var sourceCompletedTaskSource = new EventTaskSource<Action>(
                taskComplete => taskComplete,
                handler => eventSource.Completed += handler,
                handler => eventSource.Completed -= handler,
                token);

            // A task that is completed whenever GC data is received
            Task gcDataTask = Task.WhenAny(gcStartTaskSource.Task, gcBulkNodeTaskSource.Task);
            Task gcStopTask = gcStopTaskSource.Task;

            DotNetHeapDumpGraphReader dumper = new DotNetHeapDumpGraphReader(TextWriter.Null)
            {
                DotNetHeapInfo = new DotNetHeapInfo()
            };
            dumper.SetupCallbacks(_gcGraph, eventSource);

            // The event source will not always provide the GC events when it starts listening. However,
            // they will be provided when the event source is told to stop processing events. Give the
            // event source some time to produce the events, but if it doesn't start producing them within
            // a short amount of time (5 seconds), then stop processing events to allow them to be flushed.
            Task eventsTimeoutTask = Task.Delay(TimeSpan.FromSeconds(5), token);
            Task completedTask = await Task.WhenAny(gcDataTask, eventsTimeoutTask);

            token.ThrowIfCancellationRequested();

            // If started receiving GC events, wait for the GC Stop event.
            if (completedTask == gcDataTask)
            {
                await gcStopTask;
            }

            // Stop receiving events; if haven't received events yet, this will force flushing of events.
            await stopSessionAsync();

            // Wait for all received events to be processed.
            await sourceCompletedTaskSource.Task;

            // Check that GC data and stop events were received. This is done by checking that the
            // associated tasks have ran to completion. If one of them has not reached the completion state, then
            // fail the GC dump operation.
            if (gcDataTask.Status != TaskStatus.RanToCompletion ||
                gcStopTask.Status != TaskStatus.RanToCompletion)
            {
                throw new InvalidOperationException("Unable to create GC dump due to incomplete GC data.");
            }

            dumper.ConvertHeapDataToGraph();

            _gcGraph.AllowReading();
        }
    }
}
