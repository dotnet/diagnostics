﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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
            DelayPipeline timePipeline = new();
            CancellationTokenSource cancellationTokenSource = new();
            CancellationToken token = cancellationTokenSource.Token;

            await Assert.ThrowsAsync<PipelineException>(() => timePipeline.StopAsync());

            Task startTask = timePipeline.RunAsync(token);
            Task secondStartCall = timePipeline.RunAsync(token);
            Assert.Equal(startTask, secondStartCall);

            CancellationTokenSource stopSource = new();
            Task stopTask = timePipeline.StopAsync(stopSource.Token);
            Task secondStopCall = timePipeline.StopAsync(stopSource.Token);
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
            DelayPipeline timePipeline = new(TimeSpan.Zero);
            await timePipeline.RunAsync(CancellationToken.None);

            Assert.Equal(1, timePipeline.ExecutedCleanup);

            await timePipeline.DisposeAsync();

            Assert.Equal(1, timePipeline.ExecutedCleanup);
        }

        private sealed class DelayPipeline : Pipeline
        {
            public int ExecutedCleanup { get; private set; }
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
