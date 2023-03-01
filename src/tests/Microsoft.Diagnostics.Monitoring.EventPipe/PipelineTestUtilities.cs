// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
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
            TestRunner runner = await TestRunner.Create(config, outputHelper, "EventPipeTracee", testArguments);
            await runner.Start(testProcessTimeout);
            return runner;
        }

        public static async Task ExecutePipelineWithTracee(
            Pipeline pipeline,
            TestRunner testRunner,
            TaskCompletionSource<object> waitTaskSource = null)
        {
            using var cancellation = new CancellationTokenSource(DefaultPipelineRunTimeout);

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
            using var cancellation = new CancellationTokenSource(DefaultPipelineRunTimeout);

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

            // Begin event production
            testRunner.WakeupTracee();

            // Wait for event production to be done
            testRunner.WaitForSignal();

            try
            {
                // Optionally wait on caller before allowing the pipeline to stop.
                if (null != waitTaskSource)
                {
                    using var _ = token.Register(() => {
                        testRunner.WriteLine("Did not receive completion signal before cancellation.");
                        waitTaskSource.TrySetCanceled(token);
                    });

                    await waitTaskSource.Task;
                }

                //Signal for the pipeline to stop
                await pipeline.StopAsync(token);

                //After a pipeline is stopped, we should expect the RunTask to eventually finish
                await runTask;
            }
            finally
            {
                // Signal for debugee that's ok to end/move on.
                testRunner.WakeupTracee();
            }
        }
    }
}
