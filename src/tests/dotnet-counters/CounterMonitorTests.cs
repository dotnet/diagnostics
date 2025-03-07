// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.Tools;
using Microsoft.Diagnostics.Tools.Counters;
using Xunit;

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
            List<EventPipeCounterGroup> counters = CounterMonitor.ParseProviderList("MySource");
            Assert.Single(counters);
            EventPipeCounterGroup mySourceGroup = counters.First();
            Assert.Equal("MySource", mySourceGroup.ProviderName);
            Assert.False(mySourceGroup.CounterNames.Any());
        }

        [Fact]
        public void GenerateCounterListTestSingleProviderWithFilter()
        {
            List<EventPipeCounterGroup> counters = CounterMonitor.ParseProviderList("MySource[counter1,counter2,counter3]");
            Assert.Single(counters);
            EventPipeCounterGroup mySourceGroup = counters.First();
            Assert.Equal("MySource", mySourceGroup.ProviderName);
            Assert.True(Enumerable.SequenceEqual(mySourceGroup.CounterNames, new string[] { "counter1", "counter2", "counter3" }));
        }

        [Fact]
        public void GenerateCounterListTestManyProviders()
        {
            List<EventPipeCounterGroup> counters = CounterMonitor.ParseProviderList("MySource1,MySource2,System.Runtime");
            Assert.Equal(3, counters.Count());
            Assert.Equal("MySource1", counters.ElementAt(0).ProviderName);
            Assert.Equal("MySource2", counters.ElementAt(1).ProviderName);
            Assert.Equal("System.Runtime", counters.ElementAt(2).ProviderName);
        }

        [Fact]
        public void GenerateCounterListTestEventCountersPrefix()
        {
            List<EventPipeCounterGroup> counters = CounterMonitor.ParseProviderList("MySource1,EventCounters\\MySource2");
            Assert.Equal(2, counters.Count());
            Assert.Equal("MySource1", counters.ElementAt(0).ProviderName);
            Assert.Equal(CounterGroupType.All, counters.ElementAt(0).Type);
            Assert.Equal("MySource2", counters.ElementAt(1).ProviderName);
            Assert.Equal(CounterGroupType.EventCounter, counters.ElementAt(1).Type);
        }

        [Fact]
        public void GenerateCounterListTestManyProvidersWithFilter()
        {
            List<EventPipeCounterGroup> counters = CounterMonitor.ParseProviderList("MySource1[mycounter1,mycounter2], MySource2[mycounter1], System.Runtime[cpu-usage,working-set]");
            Assert.Equal(3, counters.Count());

            EventPipeCounterGroup mySource1Group = counters.ElementAt(0);
            Assert.Equal("MySource1", mySource1Group.ProviderName);
            Assert.True(Enumerable.SequenceEqual(mySource1Group.CounterNames, new string[] { "mycounter1", "mycounter2" }));

            EventPipeCounterGroup mySource2Group = counters.ElementAt(1);
            Assert.Equal("MySource2", mySource2Group.ProviderName);
            Assert.True(Enumerable.SequenceEqual(mySource2Group.CounterNames, new string[] { "mycounter1" }));

            EventPipeCounterGroup runtimeGroup = counters.ElementAt(2);
            Assert.Equal("System.Runtime", runtimeGroup.ProviderName);
            Assert.True(Enumerable.SequenceEqual(runtimeGroup.CounterNames, new string[] { "cpu-usage", "working-set" }));
        }

        [Fact]
        public void GenerateCounterListWithOptionAndArgumentsTest()
        {
            CounterMonitor monitor = new(TextWriter.Null, TextWriter.Null);
            string countersOptionText = "MyEventSource1,MyEventSource2";
            List<EventPipeCounterGroup> counters = monitor.ConfigureCounters(countersOptionText);
            Assert.Contains("MyEventSource1", counters.Select(g => g.ProviderName));
            Assert.Contains("MyEventSource2", counters.Select(g => g.ProviderName));
        }

        [Fact]
        public void ParseErrorUnbalancedBracketsInCountersArg()
        {
            CounterMonitor monitor = new(TextWriter.Null, TextWriter.Null);
            string countersOptionText = "System.Runtime[cpu-usage,MyEventSource";
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText));
            Assert.Equal("Error parsing --counters argument: Expected to find closing ']' in counter_provider", e.Message);
        }

        [Fact]
        public void ParseErrorTrailingTextInCountersArg()
        {
            CounterMonitor monitor = new(TextWriter.Null, TextWriter.Null);
            string countersOptionText = "System.Runtime[cpu-usage]hello,MyEventSource";
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText));
            Assert.Equal("Error parsing --counters argument: Unexpected characters after closing ']' in counter_provider", e.Message);
        }

        [Fact]
        public void ParseErrorEmptyProvider()
        {
            CounterMonitor monitor = new(TextWriter.Null, TextWriter.Null);
            string countersOptionText = ",MyEventSource";
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText));
            Assert.Equal("Error parsing --counters argument: Expected non-empty counter_provider", e.Message);
        }

        [Fact]
        public void ParseErrorMultipleCounterLists()
        {
            CounterMonitor monitor = new(TextWriter.Null, TextWriter.Null);
            string countersOptionText = "System.Runtime[cpu-usage][working-set],MyEventSource";
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText));
            Assert.Equal("Error parsing --counters argument: Expected at most one '[' in counter_provider", e.Message);
        }

        [Fact]
        public void ParseErrorMultiplePrefixesOnSameProvider()
        {
            CounterMonitor monitor = new(TextWriter.Null, TextWriter.Null);
            string countersOptionText = "System.Runtime,MyEventSource,EventCounters\\System.Runtime";
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText));
            Assert.Equal("Error parsing --counters argument: Using the same provider name with and without the EventCounters\\ prefix in the counter list is not supported.", e.Message);
        }
    }
}
