// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class EventPipeSessionTests
    {
        private readonly ITestOutputHelper output;

        public EventPipeSessionTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public Task BasicEventPipeSessionTest()
        {
            return BasicEventPipeSessionTestCore(useAsync: false);
        }

        [Fact]
        public Task BasicEventPipeSessionTestAsync()
        {
            return BasicEventPipeSessionTestCore(useAsync: true);
        }

        /// <summary>
        /// A simple test that checks if we can create an EventPipeSession on a child process
        /// </summary>
        private async Task BasicEventPipeSessionTestCore(bool useAsync)

        {
            using TestRunner runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(), output);
            runner.Start(timeoutInMSPipeCreation: 15_000, testProcessTimeout: 60_000);
            DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);
            using (EventPipeSession session = await clientShim.StartEventPipeSession(new List<EventPipeProvider>()
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational)
            }))
            {
                Assert.True(session.EventStream != null);
            }
            runner.Stop();
        }

        [Fact]
        public Task EventPipeSessionStreamTest()
        {
            return EventPipeSessionStreamTestCore(useAsync: false);
        }

        [Fact]
        public Task EventPipeSessionStreamTestAsync()
        {
            return EventPipeSessionStreamTestCore(useAsync: true);
        }

        /// <summary>
        /// Checks if we can create an EventPipeSession and can get some expected events out of it.
        /// </summary>
        private async Task EventPipeSessionStreamTestCore(bool useAsync)
        {
            TestRunner runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(), output);
            runner.Start(timeoutInMSPipeCreation: 15_000, testProcessTimeout: 60_000);
            DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);
            runner.PrintStatus();
            output.WriteLine($"[{DateTime.Now.ToString()}] Trying to start an EventPipe session on process {runner.Pid}");
            using (EventPipeSession session = await clientShim.StartEventPipeSession(new List<EventPipeProvider>()
            {
                new EventPipeProvider("System.Runtime", EventLevel.Informational, 0, new Dictionary<string, string>() {
                    { "EventCounterIntervalSec", "1" }
                })
            }))
            {
                int evntCnt = 0;

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
                        // This exception can happen if the target process exits while EventPipeEventSource is in the middle of reading from the pipe.
                        output.WriteLine("Error encountered while processing events");
                        output.WriteLine(e.ToString());
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

        [Fact]
        public Task EventPipeSessionUnavailableTest()
        {
            return EventPipeSessionUnavailableTestCore(useAsync: false);
        }

        [Fact]
        public Task EventPipeSessionUnavailableTestAsync()
        {
            return EventPipeSessionUnavailableTestCore(useAsync: true);
        }

        /// <summary>
        /// Tries to start an EventPipe session on a non-existent process
        /// </summary>
        private async Task EventPipeSessionUnavailableTestCore(bool useAsync)
        {
            List<int> pids = new List<int>(DiagnosticsClient.GetPublishedProcesses());
            int arbitraryPid = 1;

            DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(arbitraryPid), useAsync);

            await Assert.ThrowsAsync<ServerNotAvailableException>(() => clientShim.StartEventPipeSession(new List<EventPipeProvider>()
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational)
            }));
        }

        [Fact]
        public Task StartEventPipeSessionWithSingleProviderTest()
        {
            return StartEventPipeSessionWithSingleProviderTestCore(useAsync: false);
        }

        [Fact]
        public Task StartEventPipeSessionWithSingleProviderTestAsync()
        {
            return StartEventPipeSessionWithSingleProviderTestCore(useAsync: true);
        }

        /// <summary>
        /// Test for the method overload: public EventPipeSession StartEventPipeSession(EventPipeProvider provider, bool requestRundown=true, int circularBufferMB=256)
        /// </summary>
        private async Task StartEventPipeSessionWithSingleProviderTestCore(bool useAsync)
        {
            using TestRunner runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(), output);
            runner.Start(timeoutInMSPipeCreation: 15_000, testProcessTimeout: 60_000);
            DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);
            using (EventPipeSession session = await clientShim.StartEventPipeSession(new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational)))
            {
                Assert.True(session.EventStream != null);
            }
            runner.Stop();
        }
    }
}
