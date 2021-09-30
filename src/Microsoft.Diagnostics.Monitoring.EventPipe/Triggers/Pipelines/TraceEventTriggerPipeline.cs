// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.Pipelines
{
    /// <summary>
    /// A pipeline that detects a condition (as specified by the trigger) within the event stream
    /// of the specified event source. The callback is invoked for each instance of the detected condition.
    /// </summary>
    internal sealed class TraceEventTriggerPipeline : Pipeline
    {
        // The callback as provided to the pipeline. Invoked when the trigger condition is satisfied.
        // The trigger condition may be satisfied more than once (thus invoking the callback more than
        // once) over the lifetime of the pipeline, depending on the implementation of the trigger.
        private readonly Action<TraceEvent> _callback;

        // Completion source to help coordinate running and stopping the pipeline.
        private readonly TaskCompletionSource<object> _completionSource =
            new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The source of the trace events to monitor.
        private readonly TraceEventSource _eventSource;

        // The trigger implementation used to detect a condition in the trace event source.
        private readonly ITraceEventTrigger _trigger;

        public TraceEventTriggerPipeline(TraceEventSource eventSource, ITraceEventTrigger trigger, Action<TraceEvent> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
            _trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));

            IReadOnlyDictionary<string, IReadOnlyCollection<string>> providerEventMapFromTrigger =
                _trigger.GetProviderEventMap();

            if (null == providerEventMapFromTrigger)
            {
                // Allow all events to be forwarded to the trigger
                _eventSource.Dynamic.AddCallbackForProviderEvents(
                    null,
                    TraceEventCallback);
            }
            else
            {
                // Event providers should be compared case-insensitive whereas counter names should be compared case-sensative.
                // Make a copy of the provided map and change the comparers as appropriate.
                IDictionary<string, IEnumerable<string>> providerEventMap = providerEventMapFromTrigger.ToDictionary(
                    kvp => kvp.Key,
                    //Accept null or empty, both indicating that any event will be accepted.
                    kvp => (kvp.Value == null) ? null : (kvp.Value.Count == 0) ? null : kvp.Value.ToArray().AsEnumerable(),
                    StringComparer.OrdinalIgnoreCase);

                // Only allow events described in the mapping to be forwarded to the trigger.
                // If a provider has no events specified, then all events from that provider are forwarded.
                _eventSource.Dynamic.AddCallbackForProviderEvents(
                    (string providerName, string eventName) =>
                    {
                        if (!providerEventMap.TryGetValue(providerName, out IEnumerable<string> eventNames))
                        {
                            return EventFilterResponse.RejectProvider;
                        }
                        else if (null == eventNames)
                        {
                            return EventFilterResponse.AcceptEvent;
                        }
                        else if (!eventNames.Contains(eventName, StringComparer.Ordinal))
                        {
                            return EventFilterResponse.RejectEvent;
                        }
                        return EventFilterResponse.AcceptEvent;
                    },
                    TraceEventCallback);
            }
        }

        protected override async Task OnRun(CancellationToken token)
        {
            using var _ = token.Register(() => _completionSource.TrySetCanceled(token));

            await _completionSource.Task.ConfigureAwait(false);
        }

        protected override Task OnStop(CancellationToken token)
        {
            _completionSource.TrySetResult(null);

            return base.OnStop(token);
        }

        protected override Task OnCleanup()
        {
            _completionSource.TrySetCanceled();

            _eventSource.Dynamic.RemoveCallback<TraceEvent>(TraceEventCallback);

            return base.OnCleanup();
        }

        private void TraceEventCallback(TraceEvent obj)
        {
            // Check if processing of in-flight events should be ignored
            // due to pipeline in the midst of stopping.
            if (!_completionSource.Task.IsCompleted)
            {
                // If the trigger condition has been satified, invoke the callback
                if (_trigger.HasSatisfiedCondition(obj))
                {
                    _callback(obj);
                }
            }
        }
    }
}
