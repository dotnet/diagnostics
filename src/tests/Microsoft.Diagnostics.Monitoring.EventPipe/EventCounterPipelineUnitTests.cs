// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.NETCore.Client.UnitTests;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class EventCounterPipelineUnitTests
    {
        private readonly ITestOutputHelper _output;

        public EventCounterPipelineUnitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private sealed class TestMetricsLogger : ICountersLogger
        {
            private readonly ITestOutputHelper _output;
            private Dictionary<string, ICounterPayload> _metrics = new Dictionary<string, ICounterPayload>();

            public TestMetricsLogger(ITestOutputHelper output)
            {
                _output = output;
            }

            public IEnumerable<ICounterPayload> Metrics => _metrics.Values;

            public void Log(ICounterPayload metric)
            {
                _metrics[string.Concat(metric.Provider, "_", metric.Name)] = metric;
            }

            public void PipelineStarted()
            {
            }

            public void PipelineStopped()
            {
            }
        }

        [Fact]
        public async Task TestCounterEventPipeline()
        {
            var logger = new TestMetricsLogger(_output);
            var expectedCounters = new[] { "cpu-usage", "working-set" };
            string expectedProvider = "System.Runtime";

            await using (var testExecution = StartTraceeProcess("CounterRemoteTest"))
            {
                //TestRunner should account for start delay to make sure that the diagnostic pipe is available.

                var client = new DiagnosticsClient(testExecution.TestRunner.Pid);

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
                    RefreshInterval = TimeSpan.FromSeconds(1)
                }, new[] { logger });

                await PipelineTestUtilities.ExecutePipelineWithDebugee(pipeline, testExecution);
            }

            Assert.True(logger.Metrics.Any());

            var actualMetrics = logger.Metrics.Select(m => m.Name).OrderBy(m => m);

            Assert.Equal(expectedCounters, actualMetrics);
            Assert.True(logger.Metrics.All(m => string.Equals(m.Provider, expectedProvider)));
        }

        [Fact]
        public async Task TestStopAsync()
        {
            var logger = new TestMetricsLogger(_output);
            
            var expectedCounters = new[] { "cpu-usage", "working-set" };
            string expectedProvider = "System.Runtime";

            await using (var testExecution = StartTraceeProcess("CounterStopTest"))
            {
                //TestRunner should account for start delay to make sure that the diagnostic pipe is available.

                var client = new DiagnosticsClient(testExecution.TestRunner.Pid);

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
                    RefreshInterval = TimeSpan.FromSeconds(1)
                }, new[] { logger });

                await PipelineTestUtilities.ExecutePipelineWithDebugee(pipeline, testExecution);
            }

            var actualMetrics = logger.Metrics.Select(m => m.Name).OrderBy(m => m);
            Assert.Equal(expectedCounters, actualMetrics);
            Assert.True(logger.Metrics.All(m => string.Equals(m.Provider, expectedProvider)));
        }

        private RemoteTestExecution StartTraceeProcess(string loggerCategory)
        {
            return RemoteTestExecution.StartProcess(CommonHelper.GetTraceePathWithArgs("EventPipeTracee") + " " + loggerCategory, _output);
        }
    }
}
