// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    /// <summary>
    /// A stream that can monitor an event stream which is compatible with <see cref="EventPipeEventSource"/> for a specific event.
    /// </summary>
    internal sealed class EventMonitor : IAsyncDisposable
    {
        private readonly Action<TraceEvent> _onPayloadFilterMismatch;
        private readonly Action<TraceEvent> _onEvent;
        private readonly bool _callOnEventOnlyOnce;

        private readonly Stream _eventStream;
        private readonly bool _leaveEventStreamOpen;
        private EventPipeEventSource _eventSource;

        private readonly string _providerName;
        private readonly string _eventName;

        // The original payload filter of fieldName->fieldValue specified by the user. It will only be used to hydrate _payloadFilterIndexCache.
        private readonly IDictionary<string, string> _payloadFilter;

        // This tracks the exact indices into the provided event's payload to check for the expected values instead
        // of repeatedly searching the payload for the field names in _payloadFilter.
        private Dictionary<int, string> _payloadFilterIndexCache;

        /// <summary>
        /// A stream that can monitor an event stream which is compatible with <see cref="EventPipeEventSource"/> for a specific event.
        /// </summary>
        /// <param name="providerName">The event provider name.</param>
        /// <param name="eventName">The event name, which is the concatenation of the task name and opcode name, if set. <see cref="TraceEvent.EventName"/> for more information about the format.</param>
        /// <param name="payloadFilter">A mapping of the event payload field names to their expected values. A subset of the payload fields may be specified.</param>
        /// <param name="onEvent">A callback that will be invoked each time the requested event has been observed.</param>
        /// <param name="onPayloadFilterMismatch">A callback that will be invoked if the field names specified in <paramref name="payloadFilter"/> do not match those in the event's manifest.</param>
        /// <param name="eventStream">The source event stream which is compatible with <see cref="EventPipeEventSource"/>.</param>
        /// <param name="callOnEventOnlyOnce">If true, the provided <paramref name="onEvent"/> will only be called for the first matching event.</param>
        /// <param name="leaveEventStreamOpen">If true, the provided <paramref name="eventStream"/> will not be automatically closed when this object is disposed.</param>
        public EventMonitor(
            string providerName,
            string eventName,
            IDictionary<string, string> payloadFilter,
            Action<TraceEvent> onEvent,
            Action<TraceEvent> onPayloadFilterMismatch,
            Stream eventStream,
            bool callOnEventOnlyOnce,
            bool leaveEventStreamOpen = false) : base()
        {
            _providerName = providerName;
            _eventName = eventName;
            _onEvent = onEvent;
            _onPayloadFilterMismatch = onPayloadFilterMismatch;
            _eventStream = eventStream;
            _payloadFilter = payloadFilter;
            _callOnEventOnlyOnce = callOnEventOnlyOnce;
            _leaveEventStreamOpen = leaveEventStreamOpen;
        }

        /// <summary>
        /// Start processing the event stream, monitoring it for the requested event.
        /// This will continue to run until the event stream is complete or a stop is requested, regardless of if the specified event has been observed.
        /// </summary>
        /// <param name="token">The cancellation token.</param>
        /// <returns></returns>
        public Task ProcessAsync(CancellationToken token)
        {
            return Task.Run(() => {
                _eventSource = new EventPipeEventSource(_eventStream);
                token.ThrowIfCancellationRequested();
                using IDisposable registration = token.Register(() => _eventSource.Dispose());

                _eventSource.Dynamic.AddCallbackForProviderEvent(_providerName, _eventName, TraceEventCallback);

                _eventSource.Process();
                token.ThrowIfCancellationRequested();
            }, token);
        }

        /// <summary>
        /// Stops monitoring for the specified event.
        /// The event stream will continue to be processed until it is complete or <see cref="DisposeAsync"/> is called.
        /// </summary>
        private void StopMonitoringForEvent()
        {
            _eventSource?.Dynamic.RemoveCallback<TraceEvent>(TraceEventCallback);
        }

        private void TraceEventCallback(TraceEvent obj)
        {
            if (_payloadFilterIndexCache == null && !HydratePayloadFilterCache(obj))
            {
                // The payload filter doesn't map onto the actual data so we will never match the event.
                StopMonitoringForEvent();
                _onPayloadFilterMismatch(obj);
                return;
            }

            if (!DoesPayloadMatch(obj))
            {
                return;
            }

            if (_callOnEventOnlyOnce)
            {
                StopMonitoringForEvent();
            }

            _onEvent(obj);
        }

        /// <summary>
        /// Hydrates the payload filter cache.
        /// </summary>
        /// <param name="obj">An instance of the specified event (matching provider, task name, and opcode), but without checking the payload yet.</param>
        /// <returns></returns>
        private bool HydratePayloadFilterCache(TraceEvent obj)
        {
            if (_payloadFilterIndexCache != null)
            {
                return true;
            }

            // If there's no payload filter, there's nothing to do.
            if (_payloadFilter == null || _payloadFilter.Count == 0)
            {
                _payloadFilterIndexCache = new Dictionary<int, string>(capacity: 0);
                return true;
            }

            // If the payload has fewer fields than the requested filter, we can never match it.
            // NOTE: this function will only ever be called with an instance of the specified event
            // (matching provider, task name, and opcode) but without checking the payload yet.
            if (obj.PayloadNames.Length < _payloadFilter.Count)
            {
                return false;
            }

            Dictionary<int, string> payloadFilterCache = new(capacity: _payloadFilter.Count);
            for (int i = 0; (i < obj.PayloadNames.Length) && (payloadFilterCache.Count < _payloadFilter.Count); i++)
            {
                if (_payloadFilter.TryGetValue(obj.PayloadNames[i], out string expectedPayloadValue))
                {
                    payloadFilterCache.Add(i, expectedPayloadValue);
                }
            }

            // Check if one or more of the requested filter field names did not exist on the actual payload.
            if (_payloadFilter.Count != payloadFilterCache.Count)
            {
                return false;
            }

            _payloadFilterIndexCache = payloadFilterCache;

            return true;
        }

        private bool DoesPayloadMatch(TraceEvent obj)
        {
            foreach (var (fieldIndex, expectedValue) in _payloadFilterIndexCache)
            {
                string fieldValue = Convert.ToString(obj.PayloadValue(fieldIndex), CultureInfo.InvariantCulture) ?? string.Empty;
                if (!string.Equals(fieldValue, expectedValue, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public async ValueTask DisposeAsync()
        {
            _eventSource?.Dispose();
            if (!_leaveEventStreamOpen)
            {
                await _eventStream.DisposeAsync();
            }
        }
    }
}
