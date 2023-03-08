// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
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
            CounterMonitor monitor = new();
            CounterSet counters = CounterMonitor.ParseProviderList("MySource");
            Assert.Single(counters.Providers);
            Assert.Equal("MySource", counters.Providers.First());
            Assert.True(counters.IncludesAllCounters("MySource"));
        }

        [Fact]
        public void GenerateCounterListTestSingleProviderWithFilter()
        {
            CounterMonitor monitor = new();
            CounterSet counters = CounterMonitor.ParseProviderList("MySource[counter1,counter2,counter3]");
            Assert.Single(counters.Providers);
            Assert.Equal("MySource", counters.Providers.First());
            Assert.False(counters.IncludesAllCounters("MySource"));
            Assert.True(Enumerable.SequenceEqual(counters.GetCounters("MySource"), new string[] { "counter1", "counter2", "counter3" }));
        }

        [Fact]
        public void GenerateCounterListTestManyProviders()
        {
            CounterMonitor monitor = new();
            CounterSet counters = CounterMonitor.ParseProviderList("MySource1,MySource2,System.Runtime");
            Assert.Equal(3, counters.Providers.Count());
            Assert.Equal("MySource1", counters.Providers.ElementAt(0));
            Assert.Equal("MySource2", counters.Providers.ElementAt(1));
            Assert.Equal("System.Runtime", counters.Providers.ElementAt(2));
        }

        [Fact]
        public void GenerateCounterListTestManyProvidersWithFilter()
        {
            CounterMonitor monitor = new();
            CounterSet counters = CounterMonitor.ParseProviderList("MySource1[mycounter1,mycounter2], MySource2[mycounter1], System.Runtime[cpu-usage,working-set]");
            Assert.Equal(3, counters.Providers.Count());

            Assert.Equal("MySource1", counters.Providers.ElementAt(0));
            Assert.False(counters.IncludesAllCounters("MySource1"));
            Assert.True(Enumerable.SequenceEqual(counters.GetCounters("MySource1"), new string[] { "mycounter1", "mycounter2" }));

            Assert.Equal("MySource2", counters.Providers.ElementAt(1));
            Assert.False(counters.IncludesAllCounters("MySource2"));
            Assert.True(Enumerable.SequenceEqual(counters.GetCounters("MySource2"), new string[] { "mycounter1" }));

            Assert.Equal("System.Runtime", counters.Providers.ElementAt(2));
            Assert.False(counters.IncludesAllCounters("System.Runtime"));
            Assert.True(Enumerable.SequenceEqual(counters.GetCounters("System.Runtime"), new string[] { "cpu-usage", "working-set" }));
        }

        [Fact]
        public void GenerateCounterListWithOptionAndArgumentsTest()
        {
            CounterMonitor monitor = new();
            List<string> commandLineProviderArgs = new() { "System.Runtime", "MyEventSource" };
            string countersOptionText = "MyEventSource1,MyEventSource2";
            CounterSet counters = monitor.ConfigureCounters(countersOptionText, commandLineProviderArgs);
            Assert.Contains("MyEventSource", counters.Providers);
            Assert.Contains("MyEventSource1", counters.Providers);
            Assert.Contains("MyEventSource2", counters.Providers);
            Assert.Contains("System.Runtime", counters.Providers);
        }

        [Fact]
        public void GenerateCounterListWithOptionAndArgumentsTestWithDupEntries()
        {
            CounterMonitor monitor = new();
            List<string> commandLineProviderArgs = new() { "System.Runtime", "MyEventSource" };
            string countersOptionText = "System.Runtime,MyEventSource";
            CounterSet counters = monitor.ConfigureCounters(countersOptionText, commandLineProviderArgs);
            Assert.Equal(2, counters.Providers.Count());
            Assert.Contains("MyEventSource", counters.Providers);
            Assert.Contains("System.Runtime", counters.Providers);
        }

        [Fact]
        public void ParseErrorUnbalancedBracketsInCountersArg()
        {
            CounterMonitor monitor = new();
            string countersOptionText = "System.Runtime[cpu-usage,MyEventSource";
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText, null));
            Assert.Equal("Error parsing --counters argument: Expected to find closing ']' in counter_provider", e.Message);
        }

        [Fact]
        public void ParseErrorUnbalancedBracketsInCounterList()
        {
            CounterMonitor monitor = new();
            string countersOptionText = "System.Runtime,MyEventSource";
            List<string> commandLineProviderArgs = new() { "System.Runtime[cpu-usage", "MyEventSource" };
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText, commandLineProviderArgs));
            Assert.Equal("Error parsing counter_list: Expected to find closing ']' in counter_provider", e.Message);
        }

        [Fact]
        public void ParseErrorTrailingTextInCountersArg()
        {
            CounterMonitor monitor = new();
            string countersOptionText = "System.Runtime[cpu-usage]hello,MyEventSource";
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText, null));
            Assert.Equal("Error parsing --counters argument: Unexpected characters after closing ']' in counter_provider", e.Message);
        }

        [Fact]
        public void ParseErrorEmptyProvider()
        {
            CounterMonitor monitor = new();
            string countersOptionText = ",MyEventSource";
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText, null));
            Assert.Equal("Error parsing --counters argument: Expected non-empty counter_provider", e.Message);
        }

        [Fact]
        public void ParseErrorMultipleCounterLists()
        {
            CounterMonitor monitor = new();
            string countersOptionText = "System.Runtime[cpu-usage][working-set],MyEventSource";
            CommandLineErrorException e = Assert.Throws<CommandLineErrorException>(() => monitor.ConfigureCounters(countersOptionText, null));
            Assert.Equal("Error parsing --counters argument: Expected at most one '[' in counter_provider", e.Message);
        }
    }
}
