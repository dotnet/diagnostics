// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class EventCounterPipelineUnitTests
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public EventCounterPipelineUnitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        class ExpectedCounter
        {
            public string ProviderName { get; }
            public string CounterName { get; }
            public string MeterTags { get; }
            public string InstrumentTags { get; }

            public ExpectedCounter(string providerName, string counterName, string meterTags = null, string instrumentTags = null)
            {
                ProviderName = providerName;
                CounterName = counterName;
                MeterTags = meterTags;
                InstrumentTags = instrumentTags;
            }

            public bool MatchesCounterMetadata(CounterMetadata metadata)
            {
                if (metadata.ProviderName != ProviderName) return false;
                if (metadata.CounterName != CounterName) return false;
                if (MeterTags != null && metadata.MeterTags != MeterTags) return false;
                if (InstrumentTags != null && metadata.InstrumentTags != InstrumentTags) return false;
                return true;
            }
        }

        private sealed class TestMetricsLogger : ICountersLogger
        {
            private readonly List<ExpectedCounter> _expectedCounters = new();
            private Dictionary<ExpectedCounter, ICounterPayload> _metrics = new();
            private readonly TaskCompletionSource<object> _foundExpectedCountersSource;

            public TestMetricsLogger(IEnumerable<ExpectedCounter> expectedCounters, TaskCompletionSource<object> foundExpectedCountersSource)
            {
                _foundExpectedCountersSource = foundExpectedCountersSource;
                _expectedCounters = new(expectedCounters);
                if (_expectedCounters.Count == 0)
                {
                    foundExpectedCountersSource.SetResult(null);
                }
            }

            public IEnumerable<ICounterPayload> Metrics => _metrics.Values;

            public void Log(ICounterPayload payload)
            {
                bool isValuePayload = payload.EventType switch
                {
                    EventType.Gauge => true,
                    EventType.UpDownCounter => true,
                    EventType.Histogram => true,
                    EventType.Rate => true,
                    _ => false
                };
                if(!isValuePayload)
                {
                    return;
                }

                ExpectedCounter expectedCounter = _expectedCounters.Find(c => c.MatchesCounterMetadata(payload.CounterMetadata));
                if(expectedCounter != null)
                {
                    _expectedCounters.Remove(expectedCounter);
                    _metrics.Add(expectedCounter, payload);
                    // Complete the task source if the last expected key was removed.
                    if (_expectedCounters.Count == 0)
                    {
                        _foundExpectedCountersSource.TrySetResult(null);
                    }
                }
            }

            public Task PipelineStarted(CancellationToken token) => Task.CompletedTask;

            public Task PipelineStopped(CancellationToken token) => Task.CompletedTask;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterEventPipeline(TestConfiguration config)
        {
            string[] expectedCounters = new[] { "cpu-usage", "working-set" };
            string expectedProvider = "System.Runtime";

            TaskCompletionSource<object> foundExpectedCountersSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            TestMetricsLogger logger = new(expectedCounters.Select(name => new ExpectedCounter(expectedProvider, name)), foundExpectedCountersSource);

            await using (TestRunner testRunner = await PipelineTestUtilities.StartProcess(config, "CounterRemoteTest", _output))
            {
                DiagnosticsClient client = new(testRunner.Pid);

                await using MetricsPipeline pipeline = new(client, new MetricsPipelineSettings
                {
                    Duration = Timeout.InfiniteTimeSpan,
                    CounterGroups = new[]
                    {
                        new EventPipeCounterGroup
                        {
                            ProviderName = expectedProvider,
                            CounterNames = expectedCounters,
                            Type = CounterGroupType.EventCounter
                        }
                    },
                    CounterIntervalSeconds = 1
                }, new[] { logger });

                await PipelineTestUtilities.ExecutePipelineWithTracee(
                    pipeline,
                    testRunner,
                    foundExpectedCountersSource);
            }

            Assert.True(logger.Metrics.Any());

            IOrderedEnumerable<string> actualMetrics = logger.Metrics.Select(m => m.CounterMetadata.CounterName).OrderBy(m => m);

            Assert.Equal(expectedCounters, actualMetrics);
            Assert.True(logger.Metrics.All(m => string.Equals(m.CounterMetadata.ProviderName, expectedProvider)));
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestDuplicateNameMetrics(TestConfiguration config)
        {
            if (config.RuntimeFrameworkVersionMajor == 10)
            {
                throw new SkipTestException("MetricsEventSource currently has a bug wrt metertelemetryschemaurl. Reenable after https://github.com/dotnet/runtime/pull/113524 is in the runtime payload.");
            }

            if (config.RuntimeFrameworkVersionMajor < 9)
            {
                throw new SkipTestException("MetricsEventSource only supports instrument IDs starting in .NET 9.0.");
            }
            string providerName = "AmbiguousNameMeter";
            string counterName = "AmbiguousNameCounter";
            ExpectedCounter[] expectedCounters =
                [
                    new ExpectedCounter(providerName, counterName, "MeterTag=one","InstrumentTag=A"),
                    new ExpectedCounter(providerName, counterName, "MeterTag=one","InstrumentTag=B"),
                    new ExpectedCounter(providerName, counterName, "MeterTag=two","InstrumentTag=A"),
                    new ExpectedCounter(providerName, counterName, "MeterTag=two","InstrumentTag=B"),
                ];
            TaskCompletionSource<object> foundExpectedCountersSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TestMetricsLogger logger = new(expectedCounters, foundExpectedCountersSource);

            await using (TestRunner testRunner = await PipelineTestUtilities.StartProcess(config, "DuplicateNameMetrics", _output, testProcessTimeout: 3_000))
            {
                DiagnosticsClient client = new(testRunner.Pid);

                await using MetricsPipeline pipeline = new(client, new MetricsPipelineSettings
                {
                    Duration = Timeout.InfiniteTimeSpan,
                    CounterGroups = new[]
                    {
                        new EventPipeCounterGroup
                        {
                            ProviderName = providerName,
                            CounterNames = [counterName]
                        }
                    },
                    CounterIntervalSeconds = 1,
                    MaxTimeSeries = 1000
                }, new[] { logger });

                await PipelineTestUtilities.ExecutePipelineWithTracee(
                    pipeline,
                    testRunner,
                    foundExpectedCountersSource);
            }

            // confirm that all four tag combinations published a value
            Assert.Equal(4, logger.Metrics.Count());
        }
    }
}
