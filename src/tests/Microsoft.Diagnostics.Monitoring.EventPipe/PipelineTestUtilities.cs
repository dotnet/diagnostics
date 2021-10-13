// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client.UnitTests;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    internal static class PipelineTestUtilities
    {
        private static readonly TimeSpan DefaultPipelineRunTimeout = TimeSpan.FromMinutes(1);

        public static async Task ExecutePipelineWithDebugee(
            ITestOutputHelper outputHelper,
            Pipeline pipeline,
            RemoteTestExecution testExecution,
            TaskCompletionSource<object> waitTaskSource = null)
        {
            using var cancellation = new CancellationTokenSource(DefaultPipelineRunTimeout);

            await ExecutePipelineWithDebugee(
                outputHelper,
                pipeline,
                testExecution,
                cancellation.Token,
                waitTaskSource);
        }

        public static async Task ExecutePipelineWithDebugee<T>(
            ITestOutputHelper outputHelper,
            EventSourcePipeline<T> pipeline,
            RemoteTestExecution testExecution,
            TaskCompletionSource<object> waitTaskSource = null)
            where T : EventSourcePipelineSettings
        {
            using var cancellation = new CancellationTokenSource(DefaultPipelineRunTimeout);

            await ExecutePipelineWithDebugee(
                outputHelper,
                pipeline,
                async (p, t) => await p.StartAsync(t),
                testExecution,
                cancellation.Token,
                waitTaskSource);
        }

        public static Task ExecutePipelineWithDebugee(
            ITestOutputHelper outputHelper,
            Pipeline pipeline,
            RemoteTestExecution testExecution,
            CancellationToken token,
            TaskCompletionSource<object> waitTaskSource = null)
        {
            return ExecutePipelineWithDebugee(
                outputHelper,
                pipeline,
                (p, t) => p.RunAsync(t),
                testExecution,
                token,
                waitTaskSource);
        }

        private static async Task ExecutePipelineWithDebugee<TPipeline>(
            ITestOutputHelper outputHelper,
            TPipeline pipeline,
            Func<TPipeline, CancellationToken, Task> startPipelineAsync,
            RemoteTestExecution testExecution,
            CancellationToken token,
            TaskCompletionSource<object> waitTaskSource = null)
            where TPipeline : Pipeline
        {
            outputHelper.WriteLine("[PipelineTestUtility] Starting pipeline...");
            Task processingTask = startPipelineAsync(pipeline, token);

            //Begin event production
            outputHelper.WriteLine("[PipelineTestUtility] Notify app to start event production...");
            testExecution.SendSignal();

            //Wait for event production to be done
            outputHelper.WriteLine("[PipelineTestUtility] Wait for events to finish being produced...");
            testExecution.WaitForSignal();

            try
            {
                // Optionally wait on caller before allowing the pipeline to stop.
                if (null != waitTaskSource)
                {
                    outputHelper.WriteLine("[PipelineTestUtility] Wait for condition from caller...");
                    using var _ = token.Register(() =>
                    {
                        outputHelper.WriteLine("[PipelineTestUtility] Did not receive completion signal before cancellation.");
                        waitTaskSource.TrySetCanceled(token);
                    });

                    await waitTaskSource.Task;
                }

                //Signal for the pipeline to stop
                outputHelper.WriteLine("[PipelineTestUtility] Stopping pipeline...");
                await pipeline.StopAsync(token);

                //After a pipeline is stopped, we should expect the RunTask to eventually finish
                outputHelper.WriteLine("[PipelineTestUtility] Waiting for pipeline to finish...");
                await processingTask;
            }
            finally
            {
                //Signal for debugee that's ok to end/move on.
                outputHelper.WriteLine("[PipelineTestUtility] Notify app to shutdown...");
                testExecution.SendSignal();
            }
        }
    }
}
