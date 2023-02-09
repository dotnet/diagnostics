// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.SystemDiagnosticsMetrics
{
    internal static class SharedTriggerImplHelper
    {
        public static bool HasSatisfiedCondition(ref long? latestTicks, ref long? targetTicks, long windowTicks, long intervalTicks, ICounterPayload payload, bool passesValueFilter)
        {
            long payloadTimestampTicks = payload.Timestamp.Ticks;
            long payloadIntervalTicks = (long)(payload.Interval * TimeSpan.TicksPerSecond);

            if (!passesValueFilter)
            {
                // Series was broken; reset state.
                latestTicks = null;
                targetTicks = null;
                return false;
            }
            else if (!targetTicks.HasValue)
            {
                // This is the first event in the series. Record latest and target times.
                latestTicks = payloadTimestampTicks;
                // The target time should be the start of the first passing interval + the requisite time window.
                // The start of the first passing interval is the payload time stamp - the interval time.
                targetTicks = payloadTimestampTicks - payloadIntervalTicks + windowTicks;
            }
            else if (latestTicks.Value + (1.5 * intervalTicks) < payloadTimestampTicks)
            {
                // Detected that an event was skipped/dropped because the time between the current
                // event and the previous is more that 150% of the requested interval; consecutive
                // counter events should not have that large of an interval. Reset for current
                // event to be first event in series. Record latest and target times.
                latestTicks = payloadTimestampTicks;
                // The target time should be the start of the first passing interval + the requisite time window.
                // The start of the first passing interval is the payload time stamp - the interval time.
                targetTicks = payloadTimestampTicks - payloadIntervalTicks + windowTicks;
            }
            else
            {
                // Update latest time to the current event time.
                latestTicks = payloadTimestampTicks;
            }

            // Trigger is satisfied when the latest time is larger than the target time.
            return latestTicks >= targetTicks;
        }

        public static void SetDefaultValueFilter(ref Func<double, bool> valueFilter, double? greaterThan, double? lessThan)
        {
            if (greaterThan.HasValue)
            {
                double minValue = greaterThan.Value;
                if (lessThan.HasValue)
                {
                    double maxValue = lessThan.Value;
                    valueFilter = value => value > minValue && value < maxValue;
                }
                else
                {
                    valueFilter = value => value > minValue;
                }
            }
            else if (lessThan.HasValue)
            {
                double maxValue = lessThan.Value;
                valueFilter = value => value < maxValue;
            }
        }

        public static void SetIntervalAndWindowTicks(ref long intervalTicks, ref long windowTicks, float counterIntervalSeconds, long slidingWindowDurationTicks)
        {
            intervalTicks = (long)(counterIntervalSeconds * TimeSpan.TicksPerSecond);
            windowTicks = slidingWindowDurationTicks;
        }
    }
}
