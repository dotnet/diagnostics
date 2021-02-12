// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.Monitoring.UnitTests
{
    public class PipelineTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public PipelineTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public async Task TestStartStopCancelDispose()
        {
            var timePipeline = new DelayPipeline();
            var cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;

            await Assert.ThrowsAsync<PipelineException>(() => timePipeline.StopAsync());

            var startTask = timePipeline.RunAsync(token);
            var secondStartCall = timePipeline.RunAsync(token);
            Assert.Equal(startTask, secondStartCall);

            var stopSource = new CancellationTokenSource();
            var stopTask = timePipeline.StopAsync(stopSource.Token);
            var secondStopCall = timePipeline.StopAsync(stopSource.Token);
            Assert.Equal(stopTask, secondStopCall);

            stopSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopTask);

            cancellationTokenSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);

            await timePipeline.DisposeAsync();

            Assert.Equal(1, timePipeline.ExecutedCleanup);
        }

        [Fact]
        public async Task TestStart()
        {
            var timePipeline = new DelayPipeline(TimeSpan.Zero);
            await timePipeline.RunAsync(CancellationToken.None);

            Assert.Equal(1, timePipeline.ExecutedCleanup);

            await timePipeline.DisposeAsync();

            Assert.Equal(1, timePipeline.ExecutedCleanup);
        }

        private sealed class DelayPipeline : Pipeline
        {
            public int ExecutedCleanup { get; private set; } = 0;
            public TimeSpan Delay { get; }

            public DelayPipeline() : this(Timeout.InfiniteTimeSpan) 
            {
            }

            public DelayPipeline(TimeSpan delay)
            {
                Delay = delay;
            }

            protected override Task OnRun(CancellationToken token)
            {
                return Task.Delay(Delay, token);
            }

            protected override Task OnStop(CancellationToken token)
            {
                return Task.Delay(Delay, token);
            }

            protected override Task OnCleanup()
            {
                ExecutedCleanup++;
                return base.OnCleanup();
            }
        }
    }
}
