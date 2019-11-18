// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class EventPipeSessionTests
    {
        private string GetTraceePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "../../../Tracee/Debug/netcoreapp3.0/Tracee.exe";
            }
            return @"../../../Tracee/Debug/netcoreapp3.0/Tracee";
        }

        /// <summary>
        /// A simple test that checks if we can create an EventPipeSession on a child process
        /// </summary>
        [Fact]
        public void BasicEventPipeSessionTest()
        {
            TestRunner runner = new TestRunner(GetTraceePath());
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
            TestRunner runner = new TestRunner(GetTraceePath());
            runner.Start(3000);
            DiagnosticsClient client = new DiagnosticsClient(runner.Pid);
            using (var session = client.StartEventPipeSession(new List<EventPipeProvider>()
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational)
            }))
            {
                var source = new EventPipeEventSource(session.EventStream);
                var evntCnt = 0;
                source.Dynamic.All += (TraceEvent obj) => {
                    evntCnt += 1;
                };

                try
                {
                    source.Process();
                    Assert.True(evntCnt > 0);
                }
                // NOTE: This exception does not currently exist. It is something that needs to be added to TraceEvent.
                catch (Exception e)
                {
                    Console.WriteLine("Error encountered while processing events");
                    Assert.Equal("", e.ToString());
                }
                finally
                {
                    runner.Stop();
                }
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
