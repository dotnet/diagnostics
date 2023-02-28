// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using EventPipe.UnitTests.Common;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Xunit;
using Xunit.Abstractions;

namespace EventPipe.UnitTests.GCEventsValidation
{

    public class TestClass
    {
        public int a;
        public string b;

        public TestClass()
        {
            a = 0;
            b = "";
        }
    }
    public class GCEventsTests
    {
        private readonly ITestOutputHelper output;

        public GCEventsTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public async void GCCollect_ProducesEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => 
            {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new Dictionary<string, ExpectedEventCount>()
                {
                    { "Microsoft-Windows-DotNETRuntime", -1 },
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 },
                    { "Microsoft-DotNETCore-SampleProfiler", -1 }
                };

                var GCProviders = new List<EventPipeProvider>()
                {
                    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
                    //GCKeyword (0x1): 0b1
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 0x1)
                };

                Action _eventGeneratingAction = () => 
                {
                    for (int i = 0; i < 50; i++)
                    {
                        if (i % 10 == 0)
                        {
                            Logger.logger.Log($"Called GC.Collect() {i} times...");
                        }

                        TestClass testClass = new TestClass();
                        testClass = null;
                        GC.Collect();
                    }
                };

                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => 
                {
                    int GCStartEvents = 0;
                    int GCEndEvents = 0;
                    source.Clr.GCStart += (eventData) => GCStartEvents += 1;
                    source.Clr.GCStop += (eventData) => GCEndEvents += 1;

                    int GCRestartEEStartEvents = 0;
                    int GCRestartEEStopEvents = 0;           
                    source.Clr.GCRestartEEStart += (eventData) => GCRestartEEStartEvents += 1;
                    source.Clr.GCRestartEEStop += (eventData) => GCRestartEEStopEvents += 1; 

                    int GCSuspendEEEvents = 0;
                    int GCSuspendEEEndEvents = 0;
                    source.Clr.GCSuspendEEStart += (eventData) => GCSuspendEEEvents += 1;
                    source.Clr.GCSuspendEEStop += (eventData) => GCSuspendEEEndEvents += 1;

                    int GCHeapStatsEvents =0;
                    source.Clr.GCHeapStats += (eventData) => GCHeapStatsEvents +=1;

                    return () => {
                        Logger.logger.Log("Event counts validation");

                        Logger.logger.Log("GCStartEvents: " + GCStartEvents);
                        Logger.logger.Log("GCEndEvents: " + GCEndEvents);
                        bool GCStartStopResult = GCStartEvents >= 50 && GCEndEvents >= 50 && Math.Abs(GCStartEvents - GCEndEvents) <= 2;
                        Logger.logger.Log("GCStartStopResult check: " + GCStartStopResult);

                        Logger.logger.Log("GCRestartEEStartEvents: " + GCRestartEEStartEvents);
                        Logger.logger.Log("GCRestartEEStopEvents: " + GCRestartEEStopEvents);
                        bool GCRestartEEStartStopResult = GCRestartEEStartEvents >= 50 && GCRestartEEStopEvents >= 50;
                        Logger.logger.Log("GCRestartEEStartStopResult check: " + GCRestartEEStartStopResult);

                        Logger.logger.Log("GCSuspendEEEvents: " + GCSuspendEEEvents);
                        Logger.logger.Log("GCSuspendEEEndEvents: " + GCSuspendEEEndEvents);
                        bool GCSuspendEEStartStopResult = GCSuspendEEEvents >= 50 && GCSuspendEEEndEvents >= 50;
                        Logger.logger.Log("GCSuspendEEStartStopResult check: " + GCSuspendEEStartStopResult);

                        Logger.logger.Log("GCHeapStatsEvents: " + GCHeapStatsEvents);
                        bool GCHeapStatsEventsResult = GCHeapStatsEvents >= 50 && GCHeapStatsEvents >= 50;
                        Logger.logger.Log("GCHeapStatsEventsResult check: " + GCHeapStatsEventsResult);

                        return GCStartStopResult && GCRestartEEStartStopResult && GCSuspendEEStartStopResult && GCHeapStatsEventsResult ? 100 : -1;
                    };
                };

                var ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, GCProviders, 1024, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }

        [Fact]
        public async void GCWaitForPendingFinalizers_ProducesEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => 
            {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new Dictionary<string, ExpectedEventCount>()
                {
                    { "Microsoft-Windows-DotNETRuntime", -1 },
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 },
                    { "Microsoft-DotNETCore-SampleProfiler", -1 }
                };

                var GCProviders = new List<EventPipeProvider>()
                {
                    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
                    //GCKeyword (0x1): 0b1
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 0x1)
                };

                Action _eventGeneratingAction = () => 
                {
                    for (int i = 0; i < 50; i++)
                    {
                        if (i % 10 == 0)
                        {
                            Logger.logger.Log($"Called GC.Collect() {i} times...");
                        }

                        TestClass testClass = new TestClass();
                        testClass = null;
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                };

                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => 
                {
                    int GCFinalizersEndEvents = 0;
                    source.Clr.GCFinalizersStop += (eventData) => GCFinalizersEndEvents += 1;
                    int GCFinalizersStartEvents = 0;
                    source.Clr.GCFinalizersStart += (eventData) => GCFinalizersStartEvents += 1;
                    return () => {
                        Logger.logger.Log("Event counts validation");
                        Logger.logger.Log("GCFinalizersEndEvents: " + GCFinalizersEndEvents);
                        Logger.logger.Log("GCFinalizersStartEvents: " + GCFinalizersStartEvents);
                        return GCFinalizersEndEvents >= 50 && GCFinalizersStartEvents >= 50 ? 100 : -1;
                    };
                };
                var ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, GCProviders, 1024, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }

        [Fact]
        public async void GCCollect_ProducesVerboseEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => 
            {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new Dictionary<string, ExpectedEventCount>()
                {
                    { "Microsoft-Windows-DotNETRuntime", -1 },
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 },
                    { "Microsoft-DotNETCore-SampleProfiler", -1 }
                };

                var GCProviders = new List<EventPipeProvider>()
                {
                    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
                    //GCKeyword (0x1): 0b1
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 0x1)
                };

                Action _eventGeneratingAction = () => 
                {
                    List<string> testList = new List<string>();
                    for (int i = 0; i < 100_000_000; i ++)
                    {
                        // This test was failing (no GCFreeSegment callbacks) on x86 until this GC Collects happened.
                        if (RuntimeInformation.ProcessArchitecture == Architecture.X86)
                        {
                            if (i % 1_000_000 == 0)
                            {
                                GC.Collect();
                            }
                        }
                        string t = "Test string!";
                        testList.Add(t);
                    }
                    GC.Collect();
                };

                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => 
                {
                    int GCCreateSegmentEvents = 0;
                    int GCFreeSegmentEvents = 0;
                    source.Clr.GCCreateSegment += (eventData) => GCCreateSegmentEvents += 1;
                    source.Clr.GCFreeSegment += (eventData) => GCFreeSegmentEvents += 1;

                    int GCAllocationTickEvents = 0;
                    source.Clr.GCAllocationTick += (eventData) => GCAllocationTickEvents += 1;

                    int GCCreateConcurrentThreadEvents = 0;
                    int GCTerminateConcurrentThreadEvents = 0;
                    source.Clr.GCCreateConcurrentThread += (eventData) => GCCreateConcurrentThreadEvents += 1;
                    source.Clr.GCTerminateConcurrentThread += (eventData) => GCTerminateConcurrentThreadEvents += 1;

                    return () => {
                        Logger.logger.Log("Event counts validation");

                        Logger.logger.Log("GCCreateSegmentEvents: " + GCCreateSegmentEvents);
                        Logger.logger.Log("GCFreeSegmentEvents: " + GCFreeSegmentEvents);

                        // Disable checking GCFreeSegmentEvents on .NET 7.0 issue: https://github.com/dotnet/diagnostics/issues/3143 
                        bool GCSegmentResult = GCCreateSegmentEvents > 0 && (GCFreeSegmentEvents > 0 || Environment.Version.Major >= 7);
                        Logger.logger.Log("GCSegmentResult: " + GCSegmentResult); 

                        Logger.logger.Log("GCAllocationTickEvents: " + GCAllocationTickEvents);
                        bool GCAllocationTickResult = GCAllocationTickEvents > 0;
                        Logger.logger.Log("GCAllocationTickResult: " + GCAllocationTickResult); 

                        bool GCCollectResults = GCSegmentResult && GCAllocationTickResult;
                        Logger.logger.Log("GCCollectResults: " + GCCollectResults);

                        return GCCollectResults ? 100 : -1;
                    };
                };

                var ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, GCProviders, 1024, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }
    }
}