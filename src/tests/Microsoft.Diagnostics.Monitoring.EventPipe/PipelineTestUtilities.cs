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
        public static async Task ExecutePipelineWithDebugee(ITestOutputHelper outputHelper, Pipeline pipeline, RemoteTestExecution testExecution, TaskCompletionSource<object> waitTaskSource = null)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));

            await ExecutePipelineWithDebugee(outputHelper, pipeline, testExecution, cancellation.Token, waitTaskSource);
        }

        public static async Task ExecutePipelineWithDebugee(ITestOutputHelper outputHelper, Pipeline pipeline, RemoteTestExecution testExecution, CancellationToken token, TaskCompletionSource<object> waitTaskSource = null)
        {
            Task processingTask = pipeline.RunAsync(token);

            // Wait for event session to be established before telling target app to produce events.
            if (pipeline is IEventSourcePipelineInternal eventSourcePipeline)
            {
                await eventSourcePipeline.SessionStarted;
            }

            //Begin event production
            testExecution.SendSignal();

            //Wait for event production to be done
            testExecution.WaitForSignal();

            try
            {
                // Optionally wait on caller before allowing the pipeline to stop.
                if (null != waitTaskSource)
                {
                    using var _ = token.Register(() =>
                    {
                        outputHelper.WriteLine("Did not receive completion signal before cancellation.");
                        waitTaskSource.TrySetCanceled(token);
                    });

                    await waitTaskSource.Task;
                }

                //Signal for the pipeline to stop
                await pipeline.StopAsync(token);

                //After a pipeline is stopped, we should expect the RunTask to eventually finish
                await processingTask;
            }
            finally
            {
                //Signal for debugee that's ok to end/move on.
                testExecution.SendSignal();
            }
        }
    }
}
