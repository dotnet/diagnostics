// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

namespace DotnetCounters.UnitTests
{
    /// <summary>
    /// Tests the behavior of CounterMonitor's Collect command.
    /// </summary>
    public class CounterMonitorPayloadTests
    {
        private enum CounterTypes
        {
            Metric, Rate
        }

        private ITestOutputHelper _outputHelper;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
        private static readonly string SystemRuntimeName = "System.Runtime";
        private static readonly string TagStart = "[";

        private static HashSet<CounterTypes> ExpectedCounterTypes = new() { CounterTypes.Metric, CounterTypes.Rate };

        public CounterMonitorPayloadTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorCustomMetricsJSON(TestConfiguration configuration)
        {
            CheckFramework(configuration);

            List<MetricComponents> metricComponents = await GetCounterTraceJSON(configuration, new List<string> { Constants.TestMeterName });

            ValidateCustomMetrics(metricComponents, CountersExportFormat.json);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorCustomMetricsCSV(TestConfiguration configuration)
        {
            CheckFramework(configuration);

            List<MetricComponents> metricComponents = await GetCounterTraceCSV(configuration, new List<string> { Constants.TestMeterName });

            ValidateCustomMetrics(metricComponents, CountersExportFormat.csv);
        }

        [Theory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorSystemRuntimeMetricsJSON(TestConfiguration configuration)
        {
            List<MetricComponents> metricComponents = await GetCounterTraceJSON(configuration, new List<string> { SystemRuntimeName });

            ValidateSystemRuntimeMetrics(metricComponents);
        }

        [Theory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitorSystemRuntimeMetricsCSV(TestConfiguration configuration)
        {
            List<MetricComponents> metricComponents = await GetCounterTraceCSV(configuration, new List<string> { SystemRuntimeName });

            ValidateSystemRuntimeMetrics(metricComponents);
        }

        private void ValidateSystemRuntimeMetrics(List<MetricComponents> metricComponents)
        {
            string[] ExpectedProviders = { "System.Runtime" };
            Assert.Equal(ExpectedProviders, metricComponents.Select(c => c.ProviderName).ToHashSet());

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

            Assert.Subset(metricComponents.Select(c => c.CounterName).ToHashSet(), expectedCounterNames);

            Assert.Equal(ExpectedCounterTypes, metricComponents.Select(c => c.CounterType).ToHashSet());
        }

        private async Task<List<MetricComponents>> GetCounterTraceJSON(TestConfiguration configuration, List<string> counterList)
        {
            string path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), "json");

            Func<List<MetricComponents>> createMetricComponents = () =>
            {
                using FileStream metricsFile = File.OpenRead(path);
                JSONCounterTrace trace = JsonSerializer.Deserialize<JSONCounterTrace>(metricsFile, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var providers = trace.events.Select(e => e.provider).ToList();
                var counterNames = trace.events.Select(e => e.name).ToList();
                var counterTypes = trace.events.Select(e => e.counterType).ToList();
                var tags = trace.events.Select(e => e.tags).ToList();
                var values = trace.events.Select(e => e.value).ToList();

                return CreateMetricComponents(providers, counterNames, counterTypes, tags, values);
            };

            return await GetCounterTrace(configuration, counterList, path, CountersExportFormat.json, createMetricComponents);
        }

        private async Task<List<MetricComponents>> GetCounterTraceCSV(TestConfiguration configuration, List<string> counterList)
        {
            string path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), "csv");

            Func<List<MetricComponents>> createMetricComponents = () =>
            {
                List<string> lines = File.ReadLines(path).ToList();
                CSVExporterTests.ValidateHeaderTokens(lines[0]);
                lines.RemoveAt(0); // Trim the header

                IEnumerable<string[]> splitLines = lines.Select(l => l.Split(","));

                var providers = splitLines.Select(line => line[Constants.ProviderIndex]).ToList();
                var countersList = splitLines.Select(line => line[Constants.CounterNameIndex]).ToList();
                var counterNames = countersList.Select(counter => counter.Split(TagStart)[0]).ToList();
                var counterTypes = splitLines.Select(line => line[Constants.CounterTypeIndex]).ToList();
                var tags = GetCSVTags(countersList);
                var values = GetCSVValues(splitLines);

                return CreateMetricComponents(providers, counterNames, counterTypes, tags, values);
            };

            return await GetCounterTrace(configuration, counterList, path, CountersExportFormat.csv, createMetricComponents);
        }

        private List<MetricComponents> CreateMetricComponents(List<string> providers, List<string> counterNames, List<string> counterTypes, List<string> tags, List<double> values)
        {
            List<MetricComponents> metricComponents = new(providers.Count());

            for (int index = 0; index < providers.Count(); ++index)
            {
                CounterTypes type;
                Enum.TryParse(counterTypes[index], out type);

                metricComponents.Add(new MetricComponents()
                {
                    ProviderName = providers[index],
                    CounterName = counterNames[index],
                    CounterType = type,
                    Tags = tags[index],
                    Value = values[index]
                });
            }

            return metricComponents;
        }

        private async Task<List<MetricComponents>> GetCounterTrace(TestConfiguration configuration, List<string> counterList, string path, CountersExportFormat exportFormat, Func<List<MetricComponents>> CreateMetricComponents)
        {
            try
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
                            maxTimeSeries: 1000,
                            duration: TimeSpan.FromSeconds(10)));
                }, testRunner, source.Token);

                return CreateMetricComponents();
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

        private void ValidateCustomMetrics(List<MetricComponents> metricComponents, CountersExportFormat format)
        {
            // Currently not validating timestamp due to https://github.com/dotnet/diagnostics/issues/3905

            HashSet<string> expectedProviders = new() { Constants.TestMeterName };
            Assert.Equal(expectedProviders, metricComponents.Select(c => c.ProviderName).ToHashSet());

            HashSet<string> expectedCounterNames = new() { Constants.TestHistogramName, Constants.TestCounterName };
            Assert.Equal(expectedCounterNames, metricComponents.Select(c => c.CounterName).ToHashSet());

            Assert.Equal(ExpectedCounterTypes, metricComponents.Select(c => c.CounterType).ToHashSet());

            string tagSeparator = format == CountersExportFormat.csv ? ";" : ",";
            string tag = Constants.TagKey + "=" + Constants.TagValue + tagSeparator + Constants.PercentileKey + "=";
            HashSet<string> expectedTags = new() { $"{tag}{Constants.Quantile50}", $"{tag}{Constants.Quantile95}", $"{tag}{Constants.Quantile99}" };
            Assert.Equal(expectedTags, metricComponents.Where(c => c.CounterName == Constants.TestHistogramName).Select(c => c.Tags).Distinct());
            Assert.Empty(metricComponents.Where(c => c.CounterName == Constants.TestCounterName).Where(c => c.Tags != string.Empty));

            var actualCounterValues = metricComponents.Where(c => c.CounterName == Constants.TestCounterName).Select(c => c.Value);
            Assert.Single(actualCounterValues.Distinct());
            Assert.Equal(1, actualCounterValues.First());
            double histogramValue = Assert.Single(metricComponents.Where(c => c.CounterName == Constants.TestHistogramName).Select(c => c.Value).Distinct());
            Assert.Equal(10, histogramValue);
        }

        private List<string> GetCSVTags(List<string> countersList)
        {
            var tags = countersList.Select(counter => {
                var split = counter.Split(TagStart);
                return split.Length > 1 ? split[1].Remove(split[1].Length - 1) : string.Empty;
            }).ToList();

            return tags;
        }

        private List<double> GetCSVValues(IEnumerable<string[]> splitLines)
        {
            return splitLines.Select(line => {
                return double.TryParse(line[Constants.ValueIndex], out double val) ? val : -1;
            }).ToList();
        }

        private void CheckFramework(TestConfiguration configuration)
        {
            if (configuration.RuntimeFrameworkVersionMajor < 8)
            {
                throw new SkipTestException("Not supported on < .NET 8.0");
            }
        }

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        private sealed class MetricComponents
        {
            public string ProviderName { get; set; }
            public string CounterName { get; set; }
            public double Value { get; set; }
            public string Tags { get; set; }
            public CounterTypes CounterType { get; set; }
        }

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
