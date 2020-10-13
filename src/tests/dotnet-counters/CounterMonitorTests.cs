// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Xunit;
using Microsoft.Diagnostics.Tools.Counters;
using Microsoft.Diagnostics.Tools.Counters.Exporters;

namespace DotnetCounters.UnitTests
{
    /// <summary>
    /// These test the various internal logic in CounterMonitor
    /// </summary>
    public class CounterMonitorTests
    {
        [Fact]
        public void GenerateCounterListTestSingleProvider()
        {
            CounterMonitor monitor = new CounterMonitor();
            List<string> counterList = monitor.GenerateCounterList("MySource");
            Assert.Single(counterList);
            Assert.Equal("MySource", counterList[0]);
        }

        [Fact]
        public void GenerateCounterListTestSingleProviderWithFilter()
        {
            CounterMonitor monitor = new CounterMonitor();
            List<string> counterList = monitor.GenerateCounterList("MySource[counter1,counter2,counter3]");
            Assert.Single(counterList);
            Assert.Equal("MySource[counter1,counter2,counter3]", counterList[0]);
        }

        [Fact]
        public void GenerateCounterListTestManyProviders()
        {
            CounterMonitor monitor = new CounterMonitor();
            List<string> counterList = monitor.GenerateCounterList("MySource1,MySource2,System.Runtime");
            Assert.Equal(3, counterList.Count);
            Assert.Equal("MySource1", counterList[0]);
            Assert.Equal("MySource2", counterList[1]);
            Assert.Equal("System.Runtime", counterList[2]);
        }

        [Fact]
        public void GenerateCounterListTestManyProvidersWithFilter()
        {
            CounterMonitor monitor = new CounterMonitor();
            List<string> counterList = monitor.GenerateCounterList("MySource1[mycounter1,mycounter2], MySource2[mycounter1], System.Runtime[cpu-usage,working-set]");
            Assert.Equal(3, counterList.Count);
            Assert.Equal("MySource1[mycounter1,mycounter2]", counterList[0]);
            Assert.Equal("MySource2[mycounter1]", counterList[1]);
            Assert.Equal("System.Runtime[cpu-usage,working-set]", counterList[2]);
        }

        [Fact]
        public void GenerateCounterListWithOptionAndArgumentsTest()
        {
            CounterMonitor monitor = new CounterMonitor();
            List<string> counters = new List<string>() { "System.Runtime", "MyEventSource" };
            monitor.GenerateCounterList("MyEventSource1,MyEventSource2", counters);
            Assert.Contains("MyEventSource", counters);
            Assert.Contains("MyEventSource1", counters);
            Assert.Contains("MyEventSource2", counters);
            Assert.Contains("System.Runtime", counters);
        }

        [Fact]
        public void GenerateCounterListWithOptionAndArgumentsTestWithDupEntries()
        {
            CounterMonitor monitor = new CounterMonitor();
            List<string> counters = new List<string>() { "System.Runtime", "MyEventSource" };
            monitor.GenerateCounterList("System.Runtime,MyEventSource", counters);
            Assert.Equal(2, counters.Count);
            Assert.Contains("MyEventSource", counters);
            Assert.Contains("System.Runtime", counters);
        }
    }
}
