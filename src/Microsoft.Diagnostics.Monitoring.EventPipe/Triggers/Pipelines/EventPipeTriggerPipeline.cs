// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.Pipelines
{
    /// <summary>
    /// Starts an event pipe session using the specified configuration and 
    /// </summary>
    /// <typeparam name="TSettings">The settings type of the trace event trigger.</typeparam>
    internal sealed class EventPipeTriggerPipeline<TSettings> :
        EventSourcePipeline<EventPipeTriggerPipelineSettings<TSettings>>
    {
        // The callback as provided to the pipeline. Invoked when the trigger condition is satisfied.
        // The trigger condition may be satisfied more than once (thus invoking the callback more than
        // once) over the lifetime of the pipeline, depending on the implementation of the trigger.
        private readonly Action<TraceEvent> _callback;

        /// <summary>
        /// The pipeline used to monitor the trace event source from the event pipe using the trigger
        /// specified in the settings of the current pipeline.
        /// </summary>
        private TraceEventTriggerPipeline _pipeline;

        // The trigger implementation used to detect a condition in the trace event source.
        private ITraceEventTrigger _trigger;

        public EventPipeTriggerPipeline(DiagnosticsClient client, EventPipeTriggerPipelineSettings<TSettings> settings, Action<TraceEvent> callback) :
            base(client, settings)
        {
            if (null == Settings.Configuration)
            {
                throw new ArgumentException(FormattableString.Invariant($"The {nameof(settings.Configuration)} property on the settings must not be null."), nameof(settings));
            }

            if (null == Settings.TriggerFactory)
            {
                throw new ArgumentException(FormattableString.Invariant($"The {nameof(settings.TriggerFactory)} property on the settings must not be null."), nameof(settings));
            }

            _callback = callback;
        }

        protected override MonitoringSourceConfiguration CreateConfiguration()
        {
            return Settings.Configuration;
        }

        protected override async Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            _trigger = Settings.TriggerFactory.Create(Settings.TriggerSettings);

            _pipeline = new TraceEventTriggerPipeline(eventSource, _trigger, _callback);

            await _pipeline.RunAsync(token).ConfigureAwait(false);
        }

        protected override async Task OnStop(CancellationToken token)
        {
            if (null != _pipeline)
            {
                await _pipeline.StopAsync(token).ConfigureAwait(false);
            }
            await base.OnStop(token);
        }

        protected override async Task OnCleanup()
        {
            if (null != _pipeline)
            {
                await _pipeline.DisposeAsync().ConfigureAwait(false);
            }

            // Disposal is not part of the ITraceEventTrigger interface; check the implementation
            // of the trigger to see if it implements one of the disposal interfaces and call it.
            if (_trigger is IAsyncDisposable asyncDisposableTrigger)
            {
                await asyncDisposableTrigger.DisposeAsync().ConfigureAwait(false);
            }
            else if (_trigger is IDisposable disposableTrigger)
            {
                disposableTrigger.Dispose();
            }

            await base.OnCleanup();
        }
    }
}
