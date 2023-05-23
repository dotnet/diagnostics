// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommonTestRunner;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.Tools.Counters;
//using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace DotnetCounters.UnitTests
{
    /// <summary>
    /// These test the various internal logic in CounterMonitor
    /// </summary>
    public class CounterMonitorPayloadTests
    {
        private ITestOutputHelper _outputHelper;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

        public CounterMonitorPayloadTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorCustomMetrics(TestConfiguration configuration)
        {
            if (configuration.BuildProjectFramework != "net8.0")
            {
                throw new SkipTestException("Inapplicable framework");
            }

            CounterMonitor monitor = new CounterMonitor();
            string path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), "json");

            try
            {
                using CancellationTokenSource source = new CancellationTokenSource(DefaultTimeout);

                await using var testRunner = await TestRunnerUtilities.StartProcess(configuration, "TestCounterMonitor DiagMetrics", _outputHelper);

                DateTime startTime = DateTime.Now.ToLocalTime();
                DateTime endTime = DateTime.Now.ToLocalTime() + DefaultTimeout; // is this safe?

                await TestRunnerUtilities.ExecuteCollection((ct) => {
                    return Task.Run(async () =>
                        await monitor.Collect(
                            ct: ct,
                            counter_list: new List<string> { "TestMeter" },
                            counters: null,
                            console: new TestConsole(),
                            processId: testRunner.Pid,
                            refreshInterval: 1,
                            format: CountersExportFormat.json,
                            output: path,
                            name: null,
                            diagnosticPort: null,
                            resumeRuntime: false,
                            maxHistograms: 10,
                            maxTimeSeries: 10,
                            duration: TimeSpan.FromSeconds(10)));
                }, testRunner, source.Token);

                using FileStream metricsFile = File.OpenRead(path);



                JSONCounterTrace trace = JsonSerializer.Deserialize<JSONCounterTrace>(metricsFile, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                Assert.NotEmpty(trace.events);
                string[] ExpectedNames = { "TestHistogram (feet)", "TestCounter (dollars / 1 sec)" }; // assemble from constants
                Assert.Equal(ExpectedNames, trace.events.Select(e => e.name).Distinct());

                string[] ExpectedProviders = { "TestMeter" }; // push to constant
                Assert.Equal(ExpectedProviders, trace.events.Select(e => e.provider).Distinct());

                // Disabled temporarily due to https://github.com/dotnet/diagnostics/issues/3905
                //var eventTimestamp = DateTime.Parse(trace.events[0].timestamp);
                //Assert.True(startTime < eventTimestamp && eventTimestamp < endTime); // need to make sure that's safe

                string[] ExpectedCounterTypes = { "Metric", "Rate" }; // assemble from constants
                Assert.Equal(ExpectedCounterTypes, trace.events.Select(e => e.counterType).Distinct());

                string[] ExpectedTags = { "tag=5,Percentile=50", "tag=5,Percentile=95", "tag=5,Percentile=99" }; // might be dangerous - do we know they'll be in order?
                Assert.Equal(ExpectedTags, trace.events.Where(e => e.name.Equals("TestHistogram (feet)")).Select(e => e.tags));

                Assert.Single(trace.events.Where(e => e.name.Equals("TestCounter (dollars / 1 sec)")).Select(e => e.tags).Distinct());
                Assert.Equal(string.Empty, trace.events.Where(e => e.name.Equals("TestCounter (dollars / 1 sec)")).Select(e => e.tags).Distinct().First());

                Assert.Equal(2, trace.events.Where(e => e.name.Equals("TestCounter (dollars / 1 sec)")).Select(e => e.value).Distinct().Count());
                Assert.Equal(1, trace.events.Where(e => e.name.Equals("TestCounter (dollars / 1 sec)")).Select(e => e.value).First());

                var events = trace.events;
                var ev = events[0];

                string name = ev.name;
                double value = ev.value;
                string tags = ev.tags;
                string provider = ev.provider;
                string counterType = ev.counterType;
                string timestamp = ev.timestamp;
                


                /*CounterMonitor monitor = new();
                CounterSet counters = CounterMonitor.ParseProviderList("MySource[counter1,counter2,counter3]");
                Assert.Single(counters.Providers);
                Assert.Equal("MySource", counters.Providers.First());
                Assert.False(counters.IncludesAllCounters("MySource"));
                Assert.True(Enumerable.SequenceEqual(counters.GetCounters("MySource"), new string[] { "counter1", "counter2", "counter3" }));
                */

                // monitor --counters "HatCo.HatStore" --name CustomMetricsTest --refresh-interval 5

                return;
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch { }
            }
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorSystemRuntime(TestConfiguration configuration)
        {
            if (configuration.BuildProjectFramework != "net8.0")
            {
                throw new SkipTestException("Inapplicable framework");
            }

            CounterMonitor monitor = new CounterMonitor();
            string path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), "json");

            try
            {
                using CancellationTokenSource source = new CancellationTokenSource(DefaultTimeout);

                await using var testRunner = await TestRunnerUtilities.StartProcess(configuration, "TestCounterMonitor DiagMetrics", _outputHelper);

                await TestRunnerUtilities.ExecuteCollection((ct) => {
                    return Task.Run(async () =>
                        await monitor.Collect(
                            ct: ct,
                            counter_list: new List<string> { "System.Runtime", "TestMeter" },
                            counters: null,
                            console: new TestConsole(),
                            processId: testRunner.Pid,
                            refreshInterval: 1,
                            format: CountersExportFormat.json,
                            output: path,
                            name: null,
                            diagnosticPort: null,
                            resumeRuntime: false,
                            maxHistograms: 10,
                            maxTimeSeries: 10,
                            duration: TimeSpan.FromSeconds(10)));
                }, testRunner, source.Token);

                using FileStream metricsFile = File.OpenRead(path);

                JSONCounterTrace trace = JsonSerializer.Deserialize<JSONCounterTrace>(metricsFile, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Assert.NotEmpty(trace.events);

                var events = trace.events;
                var ev = events[0];

                string name = ev.name;
                double value = ev.value;
                string tags = ev.tags;
                string provider = ev.provider;
                string counterType = ev.counterType;
                string timestamp = ev.timestamp;



                /*CounterMonitor monitor = new();
                CounterSet counters = CounterMonitor.ParseProviderList("MySource[counter1,counter2,counter3]");
                Assert.Single(counters.Providers);
                Assert.Equal("MySource", counters.Providers.First());
                Assert.False(counters.IncludesAllCounters("MySource"));
                Assert.True(Enumerable.SequenceEqual(counters.GetCounters("MySource"), new string[] { "counter1", "counter2", "counter3" }));
                */

                return;
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch { }
            }
        }

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        private sealed class TestConsole : IConsole
        {
            private readonly TestStandardStreamWriter _outWriter;
            private readonly TestStandardStreamWriter _errorWriter;

            private sealed class TestStandardStreamWriter : IStandardStreamWriter
            {
                private StringWriter _writer = new();
                public void Write(string value) => _writer.Write(value);
                public void WriteLine(string value) => _writer.WriteLine(value);
            }

            public TestConsole()
            {
                _outWriter = new TestStandardStreamWriter();
                _errorWriter = new TestStandardStreamWriter();
            }

            public IStandardStreamWriter Out => _outWriter;

            public bool IsOutputRedirected => true;

            public IStandardStreamWriter Error => _errorWriter;

            public bool IsErrorRedirected => true;

            public bool IsInputRedirected => false;
        }
    }
}
