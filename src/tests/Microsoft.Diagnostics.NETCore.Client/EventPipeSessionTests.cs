// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class EventPipeSessionTests
    {
        private readonly ITestOutputHelper output;

        public EventPipeSessionTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        /// <summary>
        /// A simple test that checks if we can create an EventPipeSession on a child process
        /// </summary>
        [Fact]
        public void BasicEventPipeSessionTest()
        {
            TestRunner runner = new TestRunner(CommonHelper.GetTraceePath(), output);
            runner.Start(3000);
            DiagnosticsClient client = new DiagnosticsClient(runner.Pid);
            using (var session = client.StartEventPipeSession(new List<EventPipeProvider>()
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational)
            }))
            {
                Assert.True(session.EventStream != null);
            }
            runner.Stop();
        }

        /// <summary>
        /// Checks if we can create an EventPipeSession and can get some expected events out of it.
        /// </summary>
        [Fact]
        public void EventPipeSessionStreamTest()
        {
            TestRunner runner = new TestRunner(CommonHelper.GetTraceePath(), output);
            runner.Start(3000);
            DiagnosticsClient client = new DiagnosticsClient(runner.Pid);
            runner.PrintStatus();
            output.WriteLine($"[{DateTime.Now.ToString()}] Trying to start an EventPipe session on process {runner.Pid}");
            using (var session = client.StartEventPipeSession(new List<EventPipeProvider>()
            {
                new EventPipeProvider("System.Runtime", EventLevel.Informational, 0, new Dictionary<string, string>() {
                    { "EventCounterIntervalSec", "1" }
                })
            }))
            {
                var evntCnt = 0;

                Task streamTask = Task.Run(() => {
                    var source = new EventPipeEventSource(session.EventStream);
                    source.Dynamic.All += (TraceEvent obj) => {
                        output.WriteLine("Got an event");
                        evntCnt += 1;
                    };
                    try
                    {
                        source.Process();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error encountered while processing events");
                        Assert.Equal("", e.ToString());
                    }
                    finally
                    {
                        runner.Stop();
                    }
                });
                output.WriteLine("Waiting for stream Task");
                streamTask.Wait(10000);
                output.WriteLine("Done waiting for stream Task");
                Assert.True(evntCnt > 0);
            }
        }

        /// <summary>
        /// Tries to start an EventPipe session on a non-existent process
        /// </summary>
        [Fact]
        public void EventPipeSessionUnavailableTest()
        {
            List<int> pids = new List<int>(DiagnosticsClient.GetPublishedProcesses());
            int arbitraryPid = 1;

            DiagnosticsClient client = new DiagnosticsClient(arbitraryPid);

            Assert.Throws<ServerNotAvailableException>(() => client.StartEventPipeSession(new List<EventPipeProvider>()
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational)
            }));
        }
    }
}
