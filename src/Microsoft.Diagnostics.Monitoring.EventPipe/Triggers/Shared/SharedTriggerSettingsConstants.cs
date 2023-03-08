// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.EventCounter;
using Microsoft.Diagnostics.Tracing;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.Shared
{
    internal class SharedTriggerSettingsConstants
    {
        internal const float CounterIntervalSeconds_MaxValue = 24 * 60 * 60; // 1 day
        internal const float CounterIntervalSeconds_MinValue = 1; // 1 second

        internal const int Percentage_MaxValue = 100;
        internal const int Percentage_MinValue = 0;

        internal const string EitherGreaterThanLessThanMessage = "Either the " + nameof(EventCounterTriggerSettings.GreaterThan) + " field or the " + nameof(EventCounterTriggerSettings.LessThan) + " field are required.";

        internal const string GreaterThanMustBeLessThanLessThanMessage = "The " + nameof(EventCounterTriggerSettings.GreaterThan) + " field must be less than the " + nameof(EventCounterTriggerSettings.LessThan) + " field.";

        internal const string SlidingWindowDuration_MaxValue = "1.00:00:00"; // 1 day
        internal const string SlidingWindowDuration_MinValue = "00:00:01"; // 1 second
    }
}
