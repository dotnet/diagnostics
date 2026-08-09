// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class DistributedTracesPipelineUnitTests
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public DistributedTracesPipelineUnitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestTracesPipeline(TestConfiguration config)
        {
            TaskCompletionSource<object?> pipelineStartedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<object?> activityLoggedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TestActivityLogger logger = new(pipelineStartedSource, activityLoggedSource);

            await using (TestRunner testRunner = await PipelineTestUtilities.StartProcess(config, "TracesRemoteTest UseActivitySource WaitForActivitySourceListener", _output))
            {
                DiagnosticsClient client = new(testRunner.Pid);

                await using DistributedTracesPipeline pipeline = new(client, new DistributedTracesPipelineSettings
                {
                    Sources = new[] { "*" },
                    SamplingRatio = 1D,
                    Duration = Timeout.InfiniteTimeSpan,
                }, new[] { logger });

                await PipelineTestUtilities.ExecutePipelineWithTracee(
                    pipeline,
                    testRunner,
                    activityLoggedSource,
                    pipelineStartedSource.Task);
            }

            Assert.Single(logger.LoggedActivities);

            var activityData = logger.LoggedActivities[0];

            var activity = activityData.Item1;

            Assert.Equal("TestBodyCore", activity.OperationName);
            Assert.Equal("Display name", activity.DisplayName);
            Assert.NotEqual(default, activity.TraceId);
            Assert.NotEqual(default, activity.SpanId);
            Assert.Equal(default, activity.ParentSpanId);
            Assert.Equal(ActivityTraceFlags.Recorded, activity.TraceFlags);
            Assert.Equal(ActivityKind.Client, activity.Kind);
            Assert.NotEqual(default, activity.StartTimeUtc);
            Assert.NotEqual(default, activity.EndTimeUtc);
            Assert.NotEqual(TimeSpan.Zero, activity.EndTimeUtc - activity.StartTimeUtc);
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
            Assert.Equal("Error occurred", activity.StatusDescription);

            Assert.NotNull(activity.Source);
            Assert.Equal("EventPipeTracee.ActivitySource", activity.Source.Name);
            Assert.Equal("1.0.0", activity.Source.Version);

            Dictionary<string, object?> tags = activityData.Item2.ToDictionary(
                i => i.Key,
                i => i.Value);
            Assert.Equal("value1", tags["custom.tag.string"]);
            Assert.Equal("18", tags["custom.tag.int"]);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestTracesPipelineWithSamplingRatio(TestConfiguration config)
        {
            TaskCompletionSource<object?> pipelineStartedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<object?> activityLoggedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TestActivityLogger logger = new(pipelineStartedSource, activityLoggedSource);

            await using (TestRunner testRunner = await PipelineTestUtilities.StartProcess(config, "TracesRemoteTest UseActivitySource WaitForActivitySourceListener", _output))
            {
                DiagnosticsClient client = new(testRunner.Pid);

                await using DistributedTracesPipeline pipeline = new(client, new DistributedTracesPipelineSettings
                {
                    Sources = new[] { "*" },
                    SamplingRatio = 0.0D,
                    Duration = Timeout.InfiniteTimeSpan,
                }, new[] { logger });

                await PipelineTestUtilities.ExecutePipelineWithTracee(
                    pipeline,
                    testRunner,
                    activityLoggedSource,
                    pipelineStartedSource.Task);
            }

            Assert.Single(logger.LoggedActivities);

            var activityData = logger.LoggedActivities[0];

            var activity = activityData.Item1;

            Assert.Equal("TestBodyCore", activity.OperationName);
            Assert.Equal("Display name", activity.DisplayName);
            Assert.NotEqual(default, activity.TraceId);
            Assert.NotEqual(default, activity.SpanId);
            Assert.Equal(default, activity.ParentSpanId);
            Assert.Equal(ActivityTraceFlags.None, activity.TraceFlags);
            Assert.Equal(ActivityKind.Client, activity.Kind);
            Assert.NotEqual(default, activity.StartTimeUtc);
            Assert.NotEqual(default, activity.EndTimeUtc);
            Assert.NotEqual(TimeSpan.Zero, activity.EndTimeUtc - activity.StartTimeUtc);
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
            Assert.Equal("Error occurred", activity.StatusDescription);

            Assert.NotNull(activity.Source);
            Assert.Equal("EventPipeTracee.ActivitySource", activity.Source.Name);
            Assert.Equal("1.0.0", activity.Source.Version);

            Assert.Empty(activityData.Item2);
        }

        private sealed class TestActivityLogger : IActivityLogger
        {
            private readonly TaskCompletionSource<object?> _pipelineStartedSource;
            private readonly TaskCompletionSource<object?> _activityLoggedSource;

            public TestActivityLogger(
                TaskCompletionSource<object?> pipelineStartedSource,
                TaskCompletionSource<object?> activityLoggedSource)
            {
                _pipelineStartedSource = pipelineStartedSource;
                _activityLoggedSource = activityLoggedSource;
            }

            public List<(ActivityData, KeyValuePair<string, object?>[])> LoggedActivities { get; } = new();

            public void Log(
                in ActivityData activity,
                ReadOnlySpan<KeyValuePair<string, object?>> tags)
            {
                LoggedActivities.Add((activity, tags.ToArray()));
                _activityLoggedSource.TrySetResult(null);
            }

            public Task PipelineStarted(CancellationToken token)
            {
                _pipelineStartedSource.TrySetResult(null);
                return Task.CompletedTask;
            }

            public Task PipelineStopped(CancellationToken token) => Task.CompletedTask;
        }
    }
}
