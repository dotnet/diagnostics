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
using Constants = DotnetCounters.UnitTests.TestConstants;
using System.Diagnostics;

namespace DotnetCounters.UnitTests
{
    /// <summary>
    /// These test the various internal logic in CounterMonitor
    /// </summary>
    public class CounterMonitorPayloadTests
    {
        private ITestOutputHelper _outputHelper;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
        private static readonly string SystemRuntimeName = "System.Runtime";
        private static readonly string Metric = "Metric";
        private static readonly string Rate = "Rate";

        private string[] ExpectedCounterTypes = { Metric, Rate };

        public CounterMonitorPayloadTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorCustomMetricsJSON(TestConfiguration configuration)
        {
            if (configuration.BuildProjectFramework != "net8.0")
            {
                throw new SkipTestException("Inapplicable framework");
            }

            JSONCounterTrace trace = await GetCounterTraceJSON(configuration, new List<string> { Constants.TestMeterName });
            Assert.NotEmpty(trace.events);

            ValidateCustomMetrics(
                trace.events.Select(e => e.provider).ToHashSet(),
                trace.events.Select(e => e.name).ToHashSet(),
                trace.events.Select(e => e.counterType).Distinct(),
                trace.events.Where(e => e.name.Equals(Constants.TestHistogramName)).Select(e => e.tags).Where(t => !string.IsNullOrEmpty(t)).ToHashSet(),
                trace.events.Where(e => e.name.Equals(Constants.TestCounterName)).Select(e => e.tags).Where(t => !string.IsNullOrEmpty(t)).ToHashSet(),
                trace.events.Where(e => e.name.Equals(Constants.TestHistogramName)).Select(e => e.value).ToList(),
                trace.events.Where(e => e.name.Equals(Constants.TestCounterName)).Select(e => e.value).ToList(),
                CountersExportFormat.json
                );
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorCustomMetricsCSV(TestConfiguration configuration)
        {
            if (configuration.BuildProjectFramework != "net8.0")
            {
                throw new SkipTestException("Inapplicable framework");
            }

            List<string> lines = await GetCounterTraceCSV(configuration, new List<string> { Constants.TestMeterName });

            var providers = lines.Select(l => l.Split(",")[Constants.ProviderIndex]).ToHashSet();

            var countersList = lines.Select(l => l.Split(",")[Constants.CounterNameIndex]).ToList();
            var counterNames = countersList.Select(counter => counter.Split("[")[0]).ToHashSet();

            var counterTypes = lines.Select(l => l.Split(",")[Constants.CounterTypeIndex]).ToHashSet();

            var counterTags = GetCSVTags(countersList, Constants.TestCounterName);
            var histogramTags = GetCSVTags(countersList, Constants.TestHistogramName);

            var counterValues = GetCSVValues(lines, Constants.TestCounterName);
            var histogramValues = GetCSVValues(lines, Constants.TestHistogramName);

            ValidateCustomMetrics(
                providers,
                counterNames,
                counterTypes,
                histogramTags,
                counterTags,
                histogramValues,
                counterValues,
                CountersExportFormat.csv);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorSystemRuntimeMetricsJSON(TestConfiguration configuration)
        {
            JSONCounterTrace trace = await GetCounterTraceJSON(configuration, new List<string> { SystemRuntimeName });
            Assert.NotEmpty(trace.events);

            ValidateSystemRuntimeMetrics(trace.events.Select(e => e.provider).Distinct().ToHashSet(), trace.events.Select(e => e.name).Distinct().ToHashSet(), trace.events.Select(e => e.counterType).Distinct());
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorSystemRuntimeMetricsCSV(TestConfiguration configuration)
        {
            List<string> lines = await GetCounterTraceCSV(configuration, new List<string> { SystemRuntimeName });

            ValidateSystemRuntimeMetrics(
                lines.Select(l => l.Split(",")[1]).ToHashSet(),
                lines.Select(l => l.Split(",")[2]).ToHashSet(),
                lines.Select(l => l.Split(",")[3]).ToHashSet());
        }

        private void ValidateSystemRuntimeMetrics(ISet<string> actualProviders, ISet<string> actualCounterNames, IEnumerable<string> actualCounterTypes)
        {
            string[] ExpectedProviders = { "System.Runtime" };
            Assert.Equal(ExpectedProviders, actualProviders);

            // Could also just check the number of counter names
            HashSet<string> expectedCounterNames = new()
            {
                "CPU Usage (%)",
                "Working Set (MB)",
                "GC Heap Size (MB)",
                "Gen 0 GC Count (Count / 1 sec)",
                "Gen 1 GC Count (Count / 1 sec)",
                "Gen 2 GC Count (Count / 1 sec)",
                "ThreadPool Thread Count",
                "Monitor Lock Contention Count (Count / 1 sec)",
                "ThreadPool Queue Length",
                "ThreadPool Completed Work Item Count (Count / 1 sec)",
                "Allocation Rate (B / 1 sec)",
                "Number of Active Timers",
                "GC Fragmentation (%)",
                "GC Committed Bytes (MB)",
                "Exception Count (Count / 1 sec)",
                "% Time in GC since last GC (%)",
                "Gen 0 Size (B)",
                "Gen 1 Size (B)",
                "Gen 2 Size (B)",
                "LOH Size (B)",
                "POH (Pinned Object Heap) Size (B)",
                "Number of Assemblies Loaded",
                "IL Bytes Jitted (B)",
                "Number of Methods Jitted",
                "Time spent in JIT (ms / 1 sec)"
            };

            Assert.Subset(actualCounterNames, expectedCounterNames);

            string[] ExpectedCounterTypes = { Metric, Rate };
            Assert.Equal(ExpectedCounterTypes, actualCounterTypes);
        }

        private async Task<JSONCounterTrace> GetCounterTraceJSON(TestConfiguration configuration, List<string> counterList)
        {
            string path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), "json");

            try
            {
                await GetCounterTrace(configuration, counterList, path, CountersExportFormat.json);
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

        private async Task<List<string>> GetCounterTraceCSV(TestConfiguration configuration, List<string> counterList)
        {
            string path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), "csv");

            try
            {
                await GetCounterTrace(configuration, counterList, path, CountersExportFormat.csv);
                Assert.True(File.Exists(path));

                List<string> lines = File.ReadLines(path).ToList();
                CSVExporterTests.ValidateHeaderTokens(lines[0]);
                lines.RemoveAt(0); // Trim the header

                return lines;
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

        private async Task GetCounterTrace(TestConfiguration configuration, List<string> counterList, string path, CountersExportFormat exportFormat)
        {
            CounterMonitor monitor = new CounterMonitor();

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
                        format: exportFormat,
                        output: path,
                        name: null,
                        diagnosticPort: null,
                        resumeRuntime: false,
                        maxHistograms: 10,
                        maxTimeSeries: 10,
                        duration: TimeSpan.FromSeconds(10)));
            }, testRunner, source.Token);
        }

        private void ValidateCustomMetrics(ISet<string> actualProviders, ISet<string> actualCounterNames, IEnumerable<string> actualCounterTypes, ISet<string> actualHistogramTags, ISet<string> actualCounterTags, List<double> actualHistogramValues, List<double> actualCounterValues, CountersExportFormat format)
        {
            // Currently not validating timestamp due to https://github.com/dotnet/diagnostics/issues/3905

            HashSet<string> expectedProviders = new() { Constants.TestMeterName };
            Assert.Equal(expectedProviders, actualProviders);

            HashSet<string> expectedCounterNames = new() { Constants.TestHistogramName, Constants.TestCounterName };
            Assert.Equal(expectedCounterNames, actualCounterNames);

            Assert.Equal(ExpectedCounterTypes, actualCounterTypes);

            string tagSeparator = format == CountersExportFormat.csv ? ";" : ",";
            string tag = Constants.TagKey + "=" + Constants.TagValue + tagSeparator + Constants.PercentileKey + "=";
            HashSet<string> expectedTags = new() { $"{tag}50", $"{tag}95", $"{tag}99" };
            Assert.Equal(expectedTags, actualHistogramTags);
            Assert.Empty(actualCounterTags);

            Assert.Equal(2, actualCounterValues.Distinct().Count());
            Assert.Equal(1, actualCounterValues.First());
            Assert.Equal(0, actualCounterValues.Last());
            double histogramValue = Assert.Single(actualHistogramValues.Distinct());
            Assert.Equal(10, histogramValue);
        }

        private ISet<string> GetCSVTags(List<string> countersList, string counterName)
        {
            var tags = countersList.Where(counter => counter.Contains(counterName)).Select(counter => {
                var split = counter.Split("[");
                return split.Length > 1 ? split[1].Remove(split[1].Length - 1) : string.Empty;
            }).ToHashSet();
            tags.Remove(string.Empty);

            return tags;
        }

        private List<double> GetCSVValues(List<string> lines, string counterName)
        {
            return lines.Where(l => l.Split(",")[2].Contains(counterName)).Select(l => {
                return double.TryParse(l.Split(",")[Constants.ValueIndex], out double val) ? val : -1;
            }).ToList();
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
