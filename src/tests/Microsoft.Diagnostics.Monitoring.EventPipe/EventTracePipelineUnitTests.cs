// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.NETCore.Client.UnitTests;
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

        [SkippableFact]
        public async Task TestTraceStopAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                throw new SkipTestException("Unstable test on OSX");
            }

            await using (var testExecution = StartTraceeProcess("TraceStopTest"))
            {
                //TestRunner should account for start delay to make sure that the diagnostic pipe is available.

                var client = new DiagnosticsClient(testExecution.TestRunner.Pid);

                var settings = new EventTracePipelineSettings()
                {
                    Duration = Timeout.InfiniteTimeSpan,
                    ProcessId = testExecution.TestRunner.Pid,
                    Configuration = new CpuProfileConfiguration()
                };

                var buffer = new MemoryStream();

                var pipeline = new EventTracePipeline(client, settings, async (s, token) => {
                    //The buffer must be read in order to not hang. The Stop message will not be processed otherwise.
                    await s.CopyToAsync(buffer);
                });
                Task pipelineTask = pipeline.RunAsync(CancellationToken.None);

                //Add a small delay to make sure diagnostic processor had a chance to initialize
                await Task.Delay(1000);
                //Send signal to proceed with event collection
                testExecution.Start();

                try
                {
                    //Get cpu for a few seconds and then stop
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    await pipeline.StopAsync();
                }
                finally
                {
                    await pipeline.DisposeAsync();
                }

                Assert.True(buffer.Length > 0);
            }
        }

        private RemoteTestExecution StartTraceeProcess(string loggerCategory)
        {
            return RemoteTestExecution.StartProcess(CommonHelper.GetTraceePathWithArgs("EventPipeTracee") + " " + loggerCategory, _output);
        }
    }
}
