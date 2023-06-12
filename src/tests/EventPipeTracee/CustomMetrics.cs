// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Constants = DotnetCounters.UnitTests.CounterMonitorPayloadTestsConstants;

namespace EventPipeTracee
{
    internal sealed class CustomMetrics : IDisposable
    {
        private Meter _meter;
        private Counter<int> _counter;
        private Histogram<float> _histogram;

        public CustomMetrics()
        {
            _meter = new(Constants.TestMeterName);
            _counter = _meter.CreateCounter<int>(Constants.TestCounter, "dollars");
            _histogram = _meter.CreateHistogram<float>(Constants.TestHistogram, "feet");
        }

        public void IncrementCounter(int v = 1)
        {
            _counter.Add(v);
        }

        public void RecordHistogram(float v = 1.0f)
        {
            KeyValuePair<string, object> tags = new(Constants.TagKey, Constants.TagValue);
            _histogram.Record(v, tags);
        }

        public void Dispose() => _meter?.Dispose();
    }
}
