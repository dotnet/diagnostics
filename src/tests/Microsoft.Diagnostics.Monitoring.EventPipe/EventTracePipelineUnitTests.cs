// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.CommonTestRunner;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class EventTracePipelineUnitTests
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public EventTracePipelineUnitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestTraceStopAsync(TestConfiguration config)
        {
            Stream eventStream = null;
            await using (var testRunner = await PipelineTestUtilities.StartProcess(config, "TraceStopTest", _output))
            {
                var client = new DiagnosticsClient(testRunner.Pid);
                var settings = new EventTracePipelineSettings()
                {
                    Duration = Timeout.InfiniteTimeSpan,
                    Configuration = new CpuProfileConfiguration()
                };

                var foundProviderSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

                await using var pipeline = new EventTracePipeline(client, settings, async (s, token) =>
                {
                    eventStream = s;

                    using var eventSource = new EventPipeEventSource(s);
                    
                    // Dispose event source when cancelled.
                    using var _ = token.Register(() => eventSource.Dispose());

                    eventSource.Dynamic.All += (TraceEvent obj) =>
                    {
                        if (string.Equals(obj.ProviderName, MonitoringSourceConfiguration.SampleProfilerProviderName, StringComparison.OrdinalIgnoreCase))
                        {
                            foundProviderSource.TrySetResult(null);
                        }
                    };

                    await Task.Run(() => Assert.True(eventSource.Process()), token);
                });

                await PipelineTestUtilities.ExecutePipelineWithTracee(
                    pipeline,
                    testRunner,
                    foundProviderSource);
            }

            //Validate that the stream is only valid for the lifetime of the callback in the trace pipeline.
            Assert.Throws<ObjectDisposedException>(() => eventStream.Read(new byte[4], 0, 4));   
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestEventStreamCleanup(TestConfiguration config)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("Test tracee sigfaults for OSX/Linux");
            }

            Stream eventStream = null;
            using var cancellationTokenSource = new CancellationTokenSource();
            await using (var testRunner = await PipelineTestUtilities.StartProcess(config, "TestEventStreamCleanup", _output))
            {
                var client = new DiagnosticsClient(testRunner.Pid);
                var settings = new EventTracePipelineSettings()
                {
                    Duration = Timeout.InfiniteTimeSpan,
                    Configuration = new CpuProfileConfiguration()
                };

                await using var pipeline = new EventTracePipeline(client, settings, (s, token) =>
                {
                    eventStream = s; //Clients should not do this.
                    cancellationTokenSource.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                });

                await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await PipelineTestUtilities.ExecutePipelineWithTracee(
                        pipeline,
                        testRunner,
                        cancellationTokenSource.Token));
            }

            //Validate that the stream is only valid for the lifetime of the callback in the trace pipeline.
            Assert.Throws<ObjectDisposedException>(() => eventStream.Read(new byte[4], 0, 4));
        }
    }
}
