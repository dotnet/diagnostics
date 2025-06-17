// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;



namespace EventPipeTracee
{
    // The constructor overloads for Meter and Counter that take tags were added in .NET 8.0
    // so this class is only available when built against 8.0 and later
#if NET8_0_OR_GREATER
    internal sealed class DuplicateNameMetrics : IDisposable
    {
        private Meter _meter1;
        private Meter _meter2;
        private Counter<int> _counter1a;
        private Counter<int> _counter1b;
        private Counter<int> _counter2a;
        private Counter<int> _counter2b;

        public DuplicateNameMetrics()
        {
            _meter1 = new Meter("AmbiguousNameMeter", "", [new("MeterTag", "one")]);
            _meter2 = new Meter("AmbiguousNameMeter", "", [new("MeterTag", "two")]);
            _counter1a = _meter1.CreateCounter<int>("AmbiguousNameCounter", "", "", [new("InstrumentTag", "A")]);
            _counter1b = _meter1.CreateCounter<int>("AmbiguousNameCounter", "", "", [new("InstrumentTag", "B")]);
            _counter2a = _meter2.CreateCounter<int>("AmbiguousNameCounter", "", "", [new("InstrumentTag", "A")]);
            _counter2b = _meter2.CreateCounter<int>("AmbiguousNameCounter", "", "", [new("InstrumentTag", "B")]);
        }


        public void IncrementCounter(int v = 1)
        {
            _counter1a.Add(v);
            _counter1b.Add(v + 1);
            _counter2a.Add(v + 2);
            _counter2b.Add(v + 3);
        }

        public void Dispose()
        {
            _meter1?.Dispose();
            _meter2?.Dispose();
        }
    }
#endif
}
