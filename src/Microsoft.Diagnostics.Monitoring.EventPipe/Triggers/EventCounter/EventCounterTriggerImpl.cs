// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.EventCounter
{
    // The core implementation of the EventCounter trigger that processes
    // the trigger settings and evaluates the counter payload. Primary motivation
    // for the implementation is for unit testability separate from TraceEvent.
    internal sealed class EventCounterTriggerImpl
    {
        private readonly long _intervalTicks;
        private readonly Func<double, bool> _valueFilter;
        private readonly long _windowTicks;

        private long? _latestTicks;
        private long? _targetTicks;

        public EventCounterTriggerImpl(EventCounterTriggerSettings settings)
        {
            if (null == settings)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings.GreaterThan.HasValue)
            {
                double minValue = settings.GreaterThan.Value;
                if (settings.LessThan.HasValue)
                {
                    double maxValue = settings.LessThan.Value;
                    _valueFilter = value => value > minValue && value < maxValue;
                }
                else
                {
                    _valueFilter = value => value > minValue;
                }
            }
            else if (settings.LessThan.HasValue)
            {
                double maxValue = settings.LessThan.Value;
                _valueFilter = value => value < maxValue;
            }

            _intervalTicks = (long)(settings.CounterIntervalSeconds * TimeSpan.TicksPerSecond);
            _windowTicks = settings.SlidingWindowDuration.Ticks;
        }

        public bool HasSatisfiedCondition(ICounterPayload payload)
        {
            long payloadTimestampTicks = payload.Timestamp.Ticks;
            long payloadIntervalTicks = (long)(payload.Interval * TimeSpan.TicksPerSecond);

            if (!_valueFilter(payload.Value))
            {
                // Series was broken; reset state.
                _latestTicks = null;
                _targetTicks = null;
                return false;
            }
            else if (!_targetTicks.HasValue)
            {
                // This is the first event in the series. Record latest and target times.
                _latestTicks = payloadTimestampTicks;
                // The target time should be the start of the first passing interval + the requisite time window.
                // The start of the first passing interval is the payload time stamp - the interval time.
                _targetTicks = payloadTimestampTicks - payloadIntervalTicks + _windowTicks;
            }
            else if (_latestTicks.Value + (1.5 * _intervalTicks) < payloadTimestampTicks)
            {
                // Detected that an event was skipped/dropped because the time between the current
                // event and the previous is more that 150% of the requested interval; consecutive
                // counter events should not have that large of an interval. Reset for current
                // event to be first event in series. Record latest and target times.
                _latestTicks = payloadTimestampTicks;
                // The target time should be the start of the first passing interval + the requisite time window.
                // The start of the first passing interval is the payload time stamp - the interval time.
                _targetTicks = payloadTimestampTicks - payloadIntervalTicks + _windowTicks;
            }
            else
            {
                // Update latest time to the current event time.
                _latestTicks = payloadTimestampTicks;
            }

            // Trigger is satisfied when the latest time is larger than the target time.
            return _latestTicks >= _targetTicks;
        }
    }
}
