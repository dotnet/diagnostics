// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Constants = DotnetCounters.UnitTests.TestConstants;

namespace EventPipeTracee
{
    internal sealed class CustomMetrics : IDisposable
    {
        private Meter _meter;
        private Counter<int> _counter;
        private UpDownCounter<int> _upDownCounter;
        private Histogram<float> _histogram;
        private ObservableCounter<int> _observableCounter;
        private ObservableUpDownCounter<int> _observableUpDownCounter;
        private ObservableGauge<double> _observableGauge;

        public CustomMetrics()
        {
            _meter = new(Constants.TestMeterName);
            _counter = _meter.CreateCounter<int>(Constants.TestCounter, "dollars");
            _upDownCounter = _meter.CreateUpDownCounter<int>(Constants.TestUpDownCounter, "queue size");
            _histogram = _meter.CreateHistogram<float>(Constants.TestHistogram, "feet");
            _observableCounter = _meter.CreateObservableCounter<int>(Constants.TestObservableCounter, () => Random.Shared.Next(), "dollars");
            _observableUpDownCounter = _meter.CreateObservableUpDownCounter<int>(Constants.TestObservableUpDownCounter, () => Random.Shared.Next(), "queue size");
            _observableGauge = _meter.CreateObservableGauge<double>(Constants.TestObservableGauge, () => Random.Shared.NextDouble(), "temperature");
        }

        public void IncrementCounter(int v = 1)
        {
            _counter.Add(v);
        }


        public void IncrementUpDownCounter(int v = 1)
        {
            _upDownCounter.Add(v);
        }

        public void RecordHistogram(float v = 10.0f)
        {
            KeyValuePair<string, object> tags = new(Constants.TagKey, Constants.TagValue);
            _histogram.Record(v, tags);
        }

        public void Dispose() => _meter?.Dispose();
    }
}
