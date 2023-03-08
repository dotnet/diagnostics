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

        private sealed class TestMetricsLogger : ICountersLogger
        {
            private readonly List<string> _expectedCounters = new();
            private Dictionary<string, ICounterPayload> _metrics = new();
            private readonly TaskCompletionSource<object> _foundExpectedCountersSource;

            public TestMetricsLogger(IDictionary<string, IEnumerable<string>> expectedCounters, TaskCompletionSource<object> foundExpectedCountersSource)
            {
                _foundExpectedCountersSource = foundExpectedCountersSource;

                if (expectedCounters.Count > 0)
                {
                    foreach (string providerName in expectedCounters.Keys)
                    {
                        foreach (string counterName in expectedCounters[providerName])
                        {
                            _expectedCounters.Add(CreateKey(providerName, counterName));
                        }
                    }
                }
                else
                {
                    foundExpectedCountersSource.SetResult(null);
                }
            }

            public IEnumerable<ICounterPayload> Metrics => _metrics.Values;

            public void Log(ICounterPayload metric)
            {
                string key = CreateKey(metric);

                _metrics[key] = metric;

                // Complete the task source if the last expected key was removed.
                if (_expectedCounters.Remove(key) && _expectedCounters.Count == 0)
                {
                    _foundExpectedCountersSource.TrySetResult(null);
                }
            }

            public Task PipelineStarted(CancellationToken token) => Task.CompletedTask;

            public Task PipelineStopped(CancellationToken token) => Task.CompletedTask;

            private static string CreateKey(ICounterPayload payload)
            {
                return CreateKey(payload.Provider, payload.Name);
            }

            private static string CreateKey(string providerName, string counterName)
            {
                return $"{providerName}_{counterName}";
            }
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterEventPipeline(TestConfiguration config)
        {
            string[] expectedCounters = new[] { "cpu-usage", "working-set" };
            string expectedProvider = "System.Runtime";

            IDictionary<string, IEnumerable<string>> expectedMap = new Dictionary<string, IEnumerable<string>>();
            expectedMap.Add(expectedProvider, expectedCounters);

            TaskCompletionSource<object> foundExpectedCountersSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            TestMetricsLogger logger = new(expectedMap, foundExpectedCountersSource);

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
                            CounterNames = expectedCounters
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

            IOrderedEnumerable<string> actualMetrics = logger.Metrics.Select(m => m.Name).OrderBy(m => m);

            Assert.Equal(expectedCounters, actualMetrics);
            Assert.True(logger.Metrics.All(m => string.Equals(m.Provider, expectedProvider)));
        }
    }
}
