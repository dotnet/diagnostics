// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Monitoring.EventPipe.Triggers;
using System;
using Xunit;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class SlidingWindowTests
    {
        [Fact]
        public void TestSlidingWindow()
        {
            DateTime start = DateTime.UtcNow;

            SlidingWindow window = new SlidingWindow(TimeSpan.FromSeconds(30));

            window.AddDataPoint(start);
            Assert.Equal(1, window.Count);

            window.AddDataPoint(start);
            window.AddDataPoint(start);

            window.AddDataPoint(start + TimeSpan.FromSeconds(10));
            window.AddDataPoint(start + TimeSpan.FromSeconds(15));
            window.AddDataPoint(start + TimeSpan.FromSeconds(20));
            window.AddDataPoint(start + TimeSpan.FromSeconds(20.5));
            window.AddDataPoint(start + TimeSpan.FromSeconds(25));

            Assert.Equal(8, window.Count);

            window.AddDataPoint(start + TimeSpan.FromSeconds(42));
            Assert.Equal(5, window.Count);

            window.AddDataPoint(start + TimeSpan.FromSeconds(52));
            Assert.Equal(3, window.Count);

            window.AddDataPoint(start + TimeSpan.FromSeconds(100));
            Assert.Equal(1, window.Count);

            window.Clear();
            Assert.Equal(0, window.Count);
        }
    }
}
