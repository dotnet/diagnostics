﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal enum CounterType
    {
        //Same as average or mean
        Metric,

        //Same as sum
        Rate
    }

    internal interface ICounterPayload
    {
        double Value { get; }

        CounterType CounterType { get; }

        CounterMetadata CounterMetadata { get; }

        string DisplayName { get; }

        string Unit { get; }

        DateTime Timestamp { get; }

        /// <summary>
        /// The interval between counters. Note this is the actual measure of time elapsed, not the requested interval.
        /// </summary>
        float Interval { get; }

        /// <summary>
        /// Optional tags for counters. Note that normal counters use ':' as a separator character, while System.Diagnostics.Metrics use ';'.
        /// We do not immediately convert string to Dictionary, since dotnet-counters does not need this conversion.
        /// </summary>
        string ValueTags { get; }

        EventType EventType { get; }

        bool IsMeter { get; }

        int Series { get; }
    }
}
