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
        public static async Task ExecutePipelineWithDebugee(Pipeline pipeline, RemoteTestExecution testExecution)
        {
            Task processingTask = pipeline.RunAsync(CancellationToken.None);

            //Begin event production
            testExecution.SendSignal();

            //Wait for event production to be done
            testExecution.WaitForSignal();

            //Signal for the pipeline to stop
            await pipeline.StopAsync();

            //Signal for debugee that's ok to end/move on.
            testExecution.SendSignal();

            await processingTask;
        }
    }
}
