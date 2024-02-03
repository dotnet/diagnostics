// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class MetricsPipelineSettings : EventSourcePipelineSettings
    {
        public EventPipeCounterGroup[] CounterGroups { get; set; }

        //Do not use TimeSpan here since we may need to synchronize this pipeline interval
        //with a different session and want to make sure the values are identical.
        public float CounterIntervalSeconds { get; set; }

        public int MaxHistograms { get; set; }

        public int MaxTimeSeries { get; set; }

        public bool UseSharedSession { get; set; }

        // Starting in .NET 8 MetricsEventSource reports both absolute value and rate for Counter instruments
        // If this is false the pipeline will produce RatePayload objects
        // If this is true and the new absolute value field is available the pipeline will produce CounterRateAndValuePayload instead
        public bool UseCounterRateAndValuePayloads { get; set; }
    }

    [Flags]
    internal enum CounterGroupType
    {
        EventCounter = 0x1,
        Meter = 0x2,
        All = 0xFF
    }

    internal class EventPipeCounterGroup
    {
        public string ProviderName { get; set; }

        public string[] CounterNames { get; set; }

        public CounterGroupType Type { get; set; } = CounterGroupType.All;

        public float? IntervalSeconds { get; set; }
    }
}
