// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using CommonTestRunner;
using Microsoft.Diagnostics.TestHelpers;
using Xunit.Abstractions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    internal static class PipelineTestUtilities
    {
        private static readonly TimeSpan DefaultPipelineRunTimeout = TimeSpan.FromMinutes(2);

        public static async Task<TestRunner> StartProcess(TestConfiguration config, string testArguments, ITestOutputHelper outputHelper, int testProcessTimeout = 60_000)
        {
            return await TestRunnerUtilities.StartProcess(config, testArguments, outputHelper, testProcessTimeout);
        }

        public static async Task ExecutePipelineWithTracee(
            Pipeline pipeline,
            TestRunner testRunner,
            TaskCompletionSource<object> waitTaskSource = null)
        {
            using CancellationTokenSource cancellation = new(DefaultPipelineRunTimeout);

            await ExecutePipelineWithTracee(
                pipeline,
                testRunner,
                cancellation.Token,
                waitTaskSource);
        }

        public static async Task ExecutePipelineWithTracee<T>(
            EventSourcePipeline<T> pipeline,
            TestRunner testRunner,
            TaskCompletionSource<object> waitTaskSource = null)
            where T : EventSourcePipelineSettings
        {
            using CancellationTokenSource cancellation = new(DefaultPipelineRunTimeout);

            await ExecutePipelineWithTracee(
                pipeline,
                (p, t) => p.StartAsync(t),
                testRunner,
                cancellation.Token,
                waitTaskSource);
        }

        public static Task ExecutePipelineWithTracee(
            Pipeline pipeline,
            TestRunner testRunner,
            CancellationToken token,
            TaskCompletionSource<object> waitTaskSource = null)
        {
            return ExecutePipelineWithTracee(
                pipeline,
                (p, t) => Task.FromResult(p.RunAsync(t)),
                testRunner,
                token,
                waitTaskSource);
        }

        private static async Task ExecutePipelineWithTracee<TPipeline>(
            TPipeline pipeline,
            Func<TPipeline, CancellationToken, Task<Task>> startPipelineAsync,
            TestRunner testRunner,
            CancellationToken token,
            TaskCompletionSource<object> waitTaskSource = null)
            where TPipeline : Pipeline
        {
            Task runTask = await startPipelineAsync(pipeline, token);

            Func<CancellationToken, Task> waitForPipeline = async (cancellationToken) => {
                // Optionally wait on caller before allowing the pipeline to stop.
                if (null != waitTaskSource)
                {
                    using CancellationTokenRegistration _ = token.Register(() => {
                        testRunner.WriteLine("Did not receive completion signal before cancellation.");
                        waitTaskSource.TrySetCanceled(token);
                    });

                    await waitTaskSource.Task;
                }

                //Signal for the pipeline to stop
                await pipeline.StopAsync(token);
            };

            await TestRunnerUtilities.ExecuteCollection(runTask, testRunner, token, waitForPipeline);
        }
    }
}
