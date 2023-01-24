// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class CounterPipelineSettings : EventSourcePipelineSettings
    {
        public EventPipeCounterGroup[] CounterGroups { get; set; }

        //Do not use TimeSpan here since we may need to synchronize this pipeline interval
        //with a different session and want to make sure the values are identical.
        public float CounterIntervalSeconds { get; set; }

        public int MaxHistograms { get; set; }

        public int MaxTimeSeries { get; set; }
    }

    [Flags]
    internal enum CounterGroupType
    {
        EventCounter = 0x1,
        Meter = 0x2,
    }

    internal class EventPipeCounterGroup
    {
        public string ProviderName { get; set; }

        public string[] CounterNames { get; set; }

        public CounterGroupType Type { get; set; } = CounterGroupType.EventCounter | CounterGroupType.Meter;

        public float? IntervalSeconds { get; set; }
    }
}
