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
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;
using Constants = DotnetCounters.UnitTests.CounterMonitorPayloadTestsConstants;

namespace DotnetCounters.UnitTests
{
    /// <summary>
    /// These test the various internal logic in CounterMonitor
    /// </summary>
    public class CounterMonitorPayloadTests
    {
        private ITestOutputHelper _outputHelper;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
        private static readonly string TestMeterName = "TestMeter";
        private static readonly string SystemRuntimeName = "System.Runtime";
        private static readonly string Metric = "Metric";
        private static readonly string Rate = "Rate";

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

            JSONCounterTrace trace = await GetCounterTrace(configuration, new List<string> { TestMeterName });

            Assert.NotEmpty(trace.events);
            string[] ExpectedNames = { Constants.TestHistogramName, Constants.TestCounterName };
            Assert.Equal(ExpectedNames, trace.events.Select(e => e.name).Distinct());

            string[] ExpectedProviders = { TestMeterName };
            Assert.Equal(ExpectedProviders, trace.events.Select(e => e.provider).Distinct());

            // Disabled temporarily due to https://github.com/dotnet/diagnostics/issues/3905
            //var eventTimestamp = DateTime.Parse(trace.events[0].timestamp);
            //Assert.True(startTime < eventTimestamp && eventTimestamp < endTime); // need to make sure that's safe

            string[] ExpectedCounterTypes = { Metric, Rate };
            Assert.Equal(ExpectedCounterTypes, trace.events.Select(e => e.counterType).Distinct());

            HashSet<string> ExpectedTags = new(){ "tag=5,Percentile=50", "tag=5,Percentile=95", "tag=5,Percentile=99" };
            Assert.Equal(ExpectedTags, trace.events.Where(e => e.name.Equals(Constants.TestHistogramName)).Select(e => e.tags).ToHashSet());

            Assert.Single(trace.events.Where(e => e.name.Equals(Constants.TestCounterName)).Select(e => e.tags).Distinct());
            Assert.Equal(string.Empty, trace.events.Where(e => e.name.Equals(Constants.TestCounterName)).Select(e => e.tags).Distinct().First());

            Assert.Equal(2, trace.events.Where(e => e.name.Equals(Constants.TestCounterName)).Select(e => e.value).Distinct().Count());
            Assert.Equal(1, trace.events.Where(e => e.name.Equals(Constants.TestCounterName)).Select(e => e.value).First());
            Assert.Equal(0, trace.events.Where(e => e.name.Equals(Constants.TestCounterName)).Select(e => e.value).Last());

            return;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorSystemRuntimeMetrics(TestConfiguration configuration)
    {
            JSONCounterTrace trace = await GetCounterTrace(configuration, new List<string> { SystemRuntimeName });

            Assert.NotEmpty(trace.events);
            Assert.Equal(25, trace.events.Select(e => e.name).Distinct().Count());

            string[] ExpectedCounterTypes = { Metric, Rate };
            Assert.Equal(ExpectedCounterTypes, trace.events.Select(e => e.counterType).Distinct());

            return;
        }

        private async Task<JSONCounterTrace> GetCounterTrace(TestConfiguration configuration, List<string> counterList)
        {
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
                            counter_list: counterList,
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

                return trace;
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
