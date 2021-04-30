// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.NETCore.Client.UnitTests;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

        [Fact(Skip = "temp")]
        public async Task TestTraceStopAsync()
        {
            using var buffer = new MemoryStream();
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

                await using var pipeline = new EventTracePipeline(client, settings, async (s, token) =>
                {
                    await s.CopyToAsync(buffer);
                    eventStream = s;
                });

                await PipelineTestUtilities.ExecutePipelineWithDebugee(pipeline, testExecution);
            }

            //Validate that the stream is only valid for the lifetime of the callback in the trace pipeline.
            Assert.Throws<ObjectDisposedException>(() => eventStream.Read(new byte[4], 0, 4));

            Assert.True(buffer.Length > 0);

            var eventSource = new EventPipeEventSource(buffer);
            bool foundCpuProvider = false;

            eventSource.Dynamic.All += (TraceEvent obj) =>
            {
                if (string.Equals(obj.ProviderName, MonitoringSourceConfiguration.SampleProfilerProviderName, StringComparison.OrdinalIgnoreCase))
                {
                    foundCpuProvider = true;
                }
            };
            Assert.True(eventSource.Process());
            Assert.True(foundCpuProvider);
        }

        [SkippableFact(Skip = "temp")]
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

                await Assert.ThrowsAsync<OperationCanceledException>(async () => await PipelineTestUtilities.ExecutePipelineWithDebugee(pipeline, testExecution, cancellationTokenSource.Token));
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
