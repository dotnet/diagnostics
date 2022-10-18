// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    /// <summary>
    /// A stream that can monitor an event stream which is compatible with <see cref="EventPipeEventSource"/>
    /// for a specific event while also passing along the event data to a destination stream.
    /// </summary>
    public sealed class EventMonitoringPassthroughStream : Stream
    {
        private readonly Action<TraceEvent> _onPayloadFilterMismatch;
        private readonly Action<TraceEvent> _onEvent;
        private readonly bool _callOnEventOnlyOnce;

        private readonly Stream _sourceStream;
        private readonly Stream _destinationStream;
        private EventPipeEventSource _eventSource;

        private readonly string _providerName;
        private readonly string _eventName;

        // The original payload filter of fieldName->fieldValue specified by the user. It will only be used to hydrate _payloadFilterIndexCache.
        private readonly IDictionary<string, string> _payloadFilter;

        // This tracks the exact indices into the provided event's payload to check for the expected values instead
        // of repeatedly searching the payload for the field names in _payloadFilter.
        private Dictionary<int, string> _payloadFilterIndexCache;


        /// <summary>
        /// A stream that can monitor an event stream which is compatible with <see cref="EventPipeEventSource"/>
        /// for a specific event while also passing along the event data to a destination stream.
        /// </summary>
        /// <param name="providerName">The stopping event provider name.</param>
        /// <param name="eventName">The stopping event name, which is the concatenation of the task name and opcode name, if set. <see cref="TraceEvent.EventName"/> for more information about the format.</param>
        /// <param name="payloadFilter">A mapping of the stopping event payload field names to their expected values. A subset of the payload fields may be specified.</param>
        /// <param name="onEvent">A callback that will be invoked each time the requested event has been observed.</param>
        /// <param name="onPayloadFilterMismatch">A callback that will be invoked if the field names specified in <paramref name="payloadFilter"/> do not match those in the event's manifest.</param>
        /// <param name="sourceStream">The source event stream which is compatible with <see cref="EventPipeEventSource"/>.</param>
        /// <param name="destinationStream">The destination stream to write events. It must either be full duplex or be write-only.</param>
        /// <param name="bufferSize">The size of the buffer to use when writing to the <paramref name="destinationStream"/>.</param>
        /// <param name="callOnEventOnlyOnce">If true, the provided <paramref name="onEvent"/> will only be called for the first matching event.</param>
        /// <param name="leaveDestinationStreamOpen">If true, the provided <paramref name="destinationStream"/> will not be automatically closed when this class is.</param>
        public EventMonitoringPassthroughStream(
            string providerName,
            string eventName,
            IDictionary<string, string> payloadFilter,
            Action<TraceEvent> onEvent,
            Action<TraceEvent> onPayloadFilterMismatch,
            Stream sourceStream,
            Stream destinationStream,
            int bufferSize,
            bool callOnEventOnlyOnce,
            bool leaveDestinationStreamOpen) : base()
        {
            _providerName = providerName;
            _eventName = eventName;
            _onEvent = onEvent;
            _onPayloadFilterMismatch = onPayloadFilterMismatch;
            _sourceStream = sourceStream;
            _payloadFilter = payloadFilter;
            _callOnEventOnlyOnce = callOnEventOnlyOnce;

            // Wrap a buffered stream around the destination stream
            // to avoid slowing down the event processing with the data
            // passthrough unless there is significant pressure.
            _destinationStream = new BufferedStream(
                leaveDestinationStreamOpen
                    ? new StreamLeaveOpenWrapper(destinationStream)
                    : destinationStream,
                bufferSize);
        }

        /// <summary>
        /// Start processing the event stream, monitoring it for the requested event and transferring its data to the specified destination stream.
        /// This will continue to run until the event stream is complete or a stop is requested, regardless of if the requested event has been observed.
        /// </summary>
        /// <param name="token">The cancellation token.</param>
        /// <returns></returns>
        public Task ProcessAsync(CancellationToken token)
        {
            return Task.Run(() =>
            {
                _eventSource = new EventPipeEventSource(this);
                token.ThrowIfCancellationRequested();
                using IDisposable registration = token.Register(() => _eventSource.Dispose());

                _eventSource.Dynamic.AddCallbackForProviderEvent(_providerName, _eventName, TraceEventCallback);

                // The EventPipeEventSource will drive the transferring of data to the destination stream as it processes events.
                _eventSource.Process();
                token.ThrowIfCancellationRequested();
            }, token);
        }

        /// <summary>
        /// Stops monitoring for the specified stopping event. Data will continue to be written to the provided destination stream.
        /// </summary>
        private void StopMonitoringForEvent()
        {
            _eventSource?.Dynamic.RemoveCallback<TraceEvent>(TraceEventCallback);
        }

        private void TraceEventCallback(TraceEvent obj)
        {
            if (_payloadFilterIndexCache == null && !HydratePayloadFilterCache(obj))
            {
                // The payload filter doesn't map onto the actual data,
                // we'll never match the event so stop checking it
                // and proceed with just transferring the data to the destination stream.
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
        /// <param name="obj">An instance of the stopping event (matching provider, task name, and opcode), but without checking the payload yet.</param>
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
            // NOTE: this function will only ever be called with an instance of the stopping event
            // (matching provider, task name, and opcode) but without checking the payload yet.
            if (obj.PayloadNames.Length < _payloadFilter.Count)
            {
                return false;
            }

            Dictionary<int, string> payloadFilterCache = new(capacity: _payloadFilter.Count);
            for (int i = 0; (i < obj.PayloadNames.Length) && (payloadFilterCache.Count < _payloadFilter.Count); i++)
            {
                if (_payloadFilter.TryGetValue(obj.PayloadNames[i], out string payloadValue))
                {
                    payloadFilterCache.Add(i, payloadValue);
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            int bytesRead = _sourceStream.Read(buffer);
            if (bytesRead != 0)
            {
                _destinationStream.Write(buffer[..bytesRead]);
            }

            return bytesRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int bytesRead = await _sourceStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead != 0)
            {
                await _destinationStream.WriteAsync(buffer[..bytesRead], cancellationToken);
            }

            return bytesRead;
        }

        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override bool CanTimeout => _sourceStream.CanRead;
        public override bool CanRead => _sourceStream.CanRead;
        public override long Length => _sourceStream.Length;

        public override long Position { get => _sourceStream.Position; set => _sourceStream.Position = value; }
        public override int ReadTimeout { get => _sourceStream.ReadTimeout; set => _sourceStream.ReadTimeout = value; }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void CopyTo(Stream destination, int bufferSize) => throw new NotSupportedException();
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public override void Flush() => _destinationStream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _destinationStream.FlushAsync(cancellationToken);

        public override async ValueTask DisposeAsync()
        {
            _eventSource?.Dispose();
            await _sourceStream.DisposeAsync();
            await _destinationStream.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
