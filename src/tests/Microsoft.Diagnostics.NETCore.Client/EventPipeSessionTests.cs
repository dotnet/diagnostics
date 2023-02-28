// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.Tracing;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825 

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class EventPipeSessionTests
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public EventPipeSessionTests(ITestOutputHelper outputHelper)
        {
            _output = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task BasicEventPipeSessionTest(TestConfiguration config)
        {
            return BasicEventPipeSessionTestCore(config, useAsync: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task BasicEventPipeSessionTestAsync(TestConfiguration config)
        {
            return BasicEventPipeSessionTestCore(config, useAsync: true);
        }

        /// <summary>
        /// A simple test that checks if we can create an EventPipeSession on a child process
        /// </summary>
        private async Task BasicEventPipeSessionTestCore(TestConfiguration config, bool useAsync)
        {
            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            await runner.Start(testProcessTimeout: 60_000);
            DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);
            // Don't dispose of the session here because it unnecessarily hangs the test for 30 secs
            EventPipeSession session = await clientShim.StartEventPipeSession(new List<EventPipeProvider>()
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational)
            });
            Assert.True(session.EventStream != null);
            runner.Stop();
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task EventPipeSessionStreamTest(TestConfiguration config)
        {
            return EventPipeSessionStreamTestCore(config, useAsync: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task EventPipeSessionStreamTestAsync(TestConfiguration config)
        {
            return EventPipeSessionStreamTestCore(config, useAsync: true);
        }

        /// <summary>
        /// Checks if we can create an EventPipeSession and can get some expected events out of it.
        /// </summary>
        private async Task EventPipeSessionStreamTestCore(TestConfiguration config, bool useAsync)
        {
            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            await runner.Start(testProcessTimeout: 60_000);
            DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);
            runner.WriteLine($"Trying to start an EventPipe session");
            using (var session = await clientShim.StartEventPipeSession(new List<EventPipeProvider>()
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
                        runner.WriteLine("Got an event");
                        evntCnt += 1;
                    };
                    try
                    {
                        source.Process();
                    }
                    catch (Exception ex)
                    {
                        // This exception can happen if the target process exits while EventPipeEventSource is in the middle of reading from the pipe.
                        runner.WriteLine($"Error encountered while processing events {ex}");
                    }
                    finally
                    {
                        runner.WakeupTracee();
                    }
                });
                runner.WriteLine("Waiting for stream Task");
                streamTask.Wait(10000);
                runner.WriteLine("Done waiting for stream Task");
                Assert.True(evntCnt > 0);
            }
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task EventPipeSessionUnavailableTest(TestConfiguration config)
        {
            return EventPipeSessionUnavailableTestCore(config, useAsync: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task EventPipeSessionUnavailableTestAsync(TestConfiguration config)
        {
            return EventPipeSessionUnavailableTestCore(config, useAsync: true);
        }

        /// <summary>
        /// Tries to start an EventPipe session on a non-existent process
        /// </summary>
        private async Task EventPipeSessionUnavailableTestCore(TestConfiguration config, bool useAsync)
        {
            List<int> pids = new List<int>(DiagnosticsClient.GetPublishedProcesses());
            int arbitraryPid = 1;

            DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(arbitraryPid), useAsync);

            await Assert.ThrowsAsync<ServerNotAvailableException>(() => clientShim.StartEventPipeSession(new List<EventPipeProvider>()
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational)
            }));
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task StartEventPipeSessionWithSingleProviderTest(TestConfiguration config)
        {
            return StartEventPipeSessionWithSingleProviderTestCore(config, useAsync: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task StartEventPipeSessionWithSingleProviderTestAsync(TestConfiguration config)
        {
            return StartEventPipeSessionWithSingleProviderTestCore(config, useAsync: true);
        }

        /// <summary>
        /// Test for the method overload: public EventPipeSession StartEventPipeSession(EventPipeProvider provider, bool requestRundown=true, int circularBufferMB=256)
        /// </summary>
        private async Task StartEventPipeSessionWithSingleProviderTestCore(TestConfiguration config, bool useAsync)
        {
            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            await runner.Start(testProcessTimeout: 60_000);
            DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);
            // Don't dispose of the session here because it unnecessarily hangs the test for 30 secs
            EventPipeSession session = await clientShim.StartEventPipeSession(new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational));
            Assert.True(session.EventStream != null);
            runner.Stop();
        }
    }
}
