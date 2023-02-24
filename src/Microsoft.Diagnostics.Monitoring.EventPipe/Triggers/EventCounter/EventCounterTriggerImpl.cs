// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.Shared;
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

            SharedTriggerImplHelper.SetDefaultValueFilter(ref _valueFilter, settings.GreaterThan, settings.LessThan);
            SharedTriggerImplHelper.SetIntervalAndWindowTicks(ref _intervalTicks, ref _windowTicks, settings.CounterIntervalSeconds, settings.SlidingWindowDuration.Ticks);
        }

        public bool HasSatisfiedCondition(ICounterPayload payload)
        {
            return SharedTriggerImplHelper.HasSatisfiedCondition(ref _latestTicks, ref _targetTicks, _windowTicks, _intervalTicks, payload, _valueFilter(payload.Value));
        }
    }
}
