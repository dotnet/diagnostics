// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.NETCore.Client.UnitTests;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class EventTracePipelineUnitTests
    {
        private readonly ITestOutputHelper _output;

        public EventTracePipelineUnitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TestTraceStopAsync()
        {
            Stream eventStream = null;
            await using (var testExecution = StartTraceeProcess("TraceStopTest"))
            {
                //TestRunner should account for start delay to make sure that the diagnostic pipe is available.

                var client = new DiagnosticsClient(testExecution.TestRunner.Pid);
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

                await PipelineTestUtilities.ExecutePipelineWithDebugee(
                    _output,
                    pipeline,
                    testExecution,
                    foundProviderSource);
            }

            //Validate that the stream is only valid for the lifetime of the callback in the trace pipeline.
            Assert.Throws<ObjectDisposedException>(() => eventStream.Read(new byte[4], 0, 4));   
        }

        [SkippableFact]
        public async Task TestEventStreamCleanup()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("Test debugee sigfaults for OSX/Linux");
            }

            Stream eventStream = null;
            using var cancellationTokenSource = new CancellationTokenSource();
            await using (var testExecution = StartTraceeProcess("TestEventStreamCleanup"))
            {
                //TestRunner should account for start delay to make sure that the diagnostic pipe is available.

                var client = new DiagnosticsClient(testExecution.TestRunner.Pid);
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
                    async () => await PipelineTestUtilities.ExecutePipelineWithDebugee(
                        _output,
                        pipeline,
                        testExecution,
                        cancellationTokenSource.Token));
            }

            //Validate that the stream is only valid for the lifetime of the callback in the trace pipeline.
            Assert.Throws<ObjectDisposedException>(() => eventStream.Read(new byte[4], 0, 4));
        }

        private RemoteTestExecution StartTraceeProcess(string loggerCategory)
        {
            return RemoteTestExecution.StartProcess(CommonHelper.GetTraceePathWithArgs("EventPipeTracee") + " " + loggerCategory, _output);
        }
    }
}
