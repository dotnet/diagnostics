using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.NETCore.Client.UnitTests;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    internal static class PipelineTestUtilities
    {
        public static async Task ExecutePipelineWithDebugee(Pipeline pipeline, RemoteTestExecution testExecution, CancellationToken token = default)
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
