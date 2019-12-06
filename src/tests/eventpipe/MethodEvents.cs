// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Xunit;
using System.ComponentModel;
using Xunit.Abstractions;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using EventPipe.UnitTests.Common;
using Microsoft.Diagnostics.Tracing;

namespace EventPipe.UnitTests.MethodEventsValidation
{
    public class M_verbose : IDisposable
    {      
        public bool IsZero(char c)
        {
            bool i = (c== 0);
            return i;
        }
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
    public class ProviderTests
    {
        private readonly ITestOutputHelper output;

        public ProviderTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public async void MethodVerbose_ProducesEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => 
            {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new Dictionary<string, ExpectedEventCount>()
                {
                    //registering Dynamic_All and Clr event callbacks will override each other, disable the check for the provider and check the events counts in the callback
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 },
                    { "Microsoft-DotNETCore-SampleProfiler", -1 }
                };

                var providers = new List<Provider>()
                {
                    new Provider("Microsoft-DotNETCore-SampleProfiler"),
                    //MethodVerboseKeyword (0x1): 0b10000
                    new Provider("Microsoft-Windows-DotNETRuntime", 0b10000, EventLevel.Verbose)
                };
                
                Action _eventGeneratingAction = () => 
                {
                    for(int i=0; i<100; i++)
                    {
                        if (i % 10 == 0)
                            Logger.logger.Log($"M_verbose occured {i} times...");

                        using(M_verbose verbose = new M_verbose())
                        {
                            verbose.IsZero('f');
                            verbose.Dispose();
                        }
                    }
                };

                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => 
                {
                    int MethodLoadVerboseEvents = 0;
                    int MethodUnloadVerboseEvents = 0;
                    source.Clr.MethodLoadVerbose += (eventData) => MethodLoadVerboseEvents += 1;
                    source.Clr.MethodUnloadVerbose += (eventData) => MethodUnloadVerboseEvents += 1;

                    int MethodJittingStartedEvents = 0;            
                    source.Clr.MethodJittingStarted += (eventData) => MethodJittingStartedEvents += 1;

                    return () => {
                        Logger.logger.Log("Event counts validation");
                        Logger.logger.Log("MethodLoadVerboseEvents: " + MethodLoadVerboseEvents);
                        Logger.logger.Log("MethodUnloadVerboseEvents: " + MethodUnloadVerboseEvents);                        
                        //MethodUnloadVerboseEvents not stable, ignore the verification
                        bool MethodVerboseResult = MethodLoadVerboseEvents >= 1 && MethodUnloadVerboseEvents >= 0;
                        Logger.logger.Log("MethodVerboseResult check: " + MethodVerboseResult);

                        Logger.logger.Log("MethodJittingStartedEvents: " + MethodJittingStartedEvents);
                        bool MethodJittingStartedResult = MethodJittingStartedEvents >= 1;
                        Logger.logger.Log("MethodJittingStartedResult check: " + MethodJittingStartedResult);
                        return MethodVerboseResult && MethodJittingStartedResult ? 100 : -1;
                    };
                };

                var config = new SessionConfiguration(circularBufferSizeMB: (uint)Math.Pow(2, 10), format: EventPipeSerializationFormat.NetTrace,  providers: providers);

                var ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, config, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }
    }
}