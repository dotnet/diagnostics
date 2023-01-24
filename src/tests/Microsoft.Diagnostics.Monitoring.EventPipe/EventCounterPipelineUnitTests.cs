// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.TestHelpers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

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
            private readonly List<string> _expectedCounters = new List<string>();
            private Dictionary<string, ICounterPayload> _metrics = new Dictionary<string, ICounterPayload>();
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

            public void PipelineStarted()
            {
            }

            public void PipelineStopped()
            {
            }

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
            var expectedCounters = new[] { "cpu-usage", "working-set" };
            string expectedProvider = "System.Runtime";

            IDictionary<string, IEnumerable<string>> expectedMap = new Dictionary<string, IEnumerable<string>>();
            expectedMap.Add(expectedProvider, expectedCounters);

            var foundExpectedCountersSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            var logger = new TestMetricsLogger(expectedMap, foundExpectedCountersSource);

            await using (var testRunner = await PipelineTestUtilities.StartProcess(config, "CounterRemoteTest", _output))
            {
                var client = new DiagnosticsClient(testRunner.Pid);

                await using EventCounterPipeline pipeline = new EventCounterPipeline(client, new EventPipeCounterPipelineSettings
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

            var actualMetrics = logger.Metrics.Select(m => m.Name).OrderBy(m => m);

            Assert.Equal(expectedCounters, actualMetrics);
            Assert.True(logger.Metrics.All(m => string.Equals(m.Provider, expectedProvider)));
        }
    }
}
