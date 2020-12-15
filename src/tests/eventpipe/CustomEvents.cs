// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Xunit;
using System.IO;
using System.Runtime.Loader;
using System.Reflection;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using EventPipe.UnitTests.Common;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace EventPipe.UnitTests.CustomEventsValidation
{
    public class MyEventSource : EventSource
    {
        public static MyEventSource Log = new MyEventSource();

        public void Event1() { WriteEvent(1); }
        public void Event2(string fileName) { WriteEvent(2, fileName); }
        public void Event3() { WriteEvent(3); }
    }

    public class CustomEventTests
    {
        private readonly ITestOutputHelper output;

        public CustomEventTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public async void CustomEventProducesEventsWithNoKeywords()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => 
            {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new Dictionary<string, ExpectedEventCount>()
                {
                    { "MyEventSource", -1 },
                };

                Action _eventGeneratingAction = () => 
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        MyEventSource.Log.Event1();
                        MyEventSource.Log.Event2("anotherFile");
                        MyEventSource.Log.Event3();
                    }
                };
 
                var providers = new List<EventPipeProvider>()
                {
                    new EventPipeProvider("MyEventSource", EventLevel.Informational)
                };

                var ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, providers, 1024, null);
                Assert.Equal(100, ret);
            }, output);
        }
    }
}
