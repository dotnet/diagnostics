// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using EventPipe.UnitTests.Common;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Xunit;
using Xunit.Abstractions;

namespace EventPipe.UnitTests.ThreadPoolValidation
{
    public class ThreadPoolEventsTests
    {
        private readonly ITestOutputHelper output;

        public ThreadPoolEventsTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public async void ThreadPool_ProducesEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new Dictionary<string, ExpectedEventCount>()
                {
                    { "Microsoft-Windows-DotNETRuntime", -1 },
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 }
                };

                var providers = new List<EventPipeProvider>()
                {
                    //ThreadingKeyword (0x10000): 0b10000_0000_0000_0000
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0b10000_0000_0000_0000)
                };

                Action _eventGeneratingAction = () => {
                    Task[] taskArray = new Task[1000];
                    for (int i = 0; i < 1000; i++)
                    {
                        if (i % 10 == 0)
                        {
                            Logger.logger.Log($"Create new task {i} times...");
                        }

                        taskArray[i] = Task.Run(() => TestTask());
                    }
                    Task.WaitAll(taskArray);
                };

                void TestTask()
                {
                    Thread.Sleep(100);
                }

                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => {
                    int ThreadStartEvents = 0;
                    source.Clr.ThreadPoolWorkerThreadStart += (eventData) => ThreadStartEvents += 1;

                    int ThreadPoolWorkerThreadAdjustmentSampleEvents = 0;
                    int ThreadPoolWorkerThreadAdjustmentAdjustmentEvents = 0;
                    source.Clr.ThreadPoolWorkerThreadAdjustmentSample += (eventData) => ThreadPoolWorkerThreadAdjustmentSampleEvents += 1;
                    source.Clr.ThreadPoolWorkerThreadAdjustmentAdjustment += (eventData) => ThreadPoolWorkerThreadAdjustmentAdjustmentEvents += 1;

                    return () => {
                        Logger.logger.Log("Event counts validation");

                        Logger.logger.Log("ThreadStartEvents: " + ThreadStartEvents);
                        bool ThreadStartStopResult = ThreadStartEvents >= 1;
                        Logger.logger.Log("ThreadStartStopResult check: " + ThreadStartStopResult);

                        Logger.logger.Log("ThreadPoolWorkerThreadAdjustmentSampleEvents: " + ThreadPoolWorkerThreadAdjustmentSampleEvents);
                        Logger.logger.Log("ThreadPoolWorkerThreadAdjustmentAdjustmentEvents: " + ThreadPoolWorkerThreadAdjustmentAdjustmentEvents);
                        bool ThreadAdjustmentResult = ThreadPoolWorkerThreadAdjustmentSampleEvents >= 1 && ThreadPoolWorkerThreadAdjustmentAdjustmentEvents >= 1;
                        Logger.logger.Log("ThreadAdjustmentResult check: " + ThreadAdjustmentResult);

                        return ThreadStartStopResult && ThreadAdjustmentResult ? 100 : -1;
                    };
                };
                var ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, providers, 1024, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }
    }
}
