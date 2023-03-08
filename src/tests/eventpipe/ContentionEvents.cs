// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using EventPipe.UnitTests.Common;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Xunit;
using Xunit.Abstractions;

namespace EventPipe.UnitTests.ContentionValidation
{

    public class TestClass
    {
        public int a;
        public static void DoSomething(TestClass obj)
        {
            lock (obj)
            {
                obj.a = 3;
                Thread.Sleep(100);
            }
        }
    }
    public class ContentionEventsTests
    {
        private readonly ITestOutputHelper output;

        public ContentionEventsTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public async void Contention_ProducesEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new()
                {
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 }
                };

                List<EventPipeProvider> providers = new()
                {
                    //ContentionKeyword (0x4000): 0b100_0000_0000_0000
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0b100_0000_0000_0000)
                };

                Action _eventGeneratingAction = () => {
                    for (int i = 0; i < 50; i++)
                    {
                        if (i % 10 == 0)
                        {
                            Logger.logger.Log($"Thread lock occured {i} times...");
                        }

                        TestClass myobject = new();
                        Thread thread1 = new(new ThreadStart(() => TestClass.DoSomething(myobject)));
                        Thread thread2 = new(new ThreadStart(() => TestClass.DoSomething(myobject)));
                        thread1.Start();
                        thread2.Start();
                        thread1.Join();
                        thread2.Join();
                    }
                };

                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => {
                    int ContentionStartEvents = 0;
                    source.Clr.ContentionStart += (eventData) => ContentionStartEvents += 1;
                    int ContentionStopEvents = 0;
                    source.Clr.ContentionStop += (eventData) => ContentionStopEvents += 1;
                    return () => {
                        Logger.logger.Log("Event counts validation");
                        Logger.logger.Log("ContentionStartEvents: " + ContentionStartEvents);
                        Logger.logger.Log("ContentionStopEvents: " + ContentionStopEvents);
                        return ContentionStartEvents > 0 && ContentionStopEvents > 0 ? 100 : -1;
                    };
                };
                int ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, providers, 1024, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }
    }
}
