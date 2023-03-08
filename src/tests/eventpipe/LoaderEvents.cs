// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using EventPipe.UnitTests.Common;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Xunit;
using Xunit.Abstractions;

namespace EventPipe.UnitTests.LoaderEventsValidation
{
    public class AssemblyLoad : AssemblyLoadContext
    {
        public AssemblyLoad() : base(true)
        {
        }
    }
    public class LoaderEventsTests
    {
        private readonly ITestOutputHelper output;

        public LoaderEventsTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public async void AssemblyLoad_ProducesEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new()
                {
                    { "Microsoft-Windows-DotNETRuntime", -1 },
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 }
                };

                List<EventPipeProvider> providers = new()
                {
                    //LoaderKeyword (0x8): 0b1000
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0b1000)
                };

                string assemblyPath = null;
                Action _eventGeneratingAction = () => {
                    GetAssemblyPath();
                    try
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            if (i % 10 == 0)
                            {
                                Logger.logger.Log($"Load/Unload Assembly {i} times...");
                            }

                            AssemblyLoad assemblyLoad = new();
                            assemblyLoad.LoadFromAssemblyPath(assemblyPath + "\\Microsoft.Diagnostics.Runtime.dll");
                            assemblyLoad.Unload();
                        }
                        GC.Collect();
                    }
                    catch (Exception ex)
                    {
                        Logger.logger.Log(ex.Message + ex.StackTrace);
                    }
                };

                void GetAssemblyPath()
                {
                    assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                }


                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => {
                    int LoaderAssemblyLoadEvents = 0;
                    int LoaderAssemblyUnloadEvents = 0;
                    source.Clr.LoaderAssemblyLoad += (eventData) => LoaderAssemblyLoadEvents += 1;
                    source.Clr.LoaderAssemblyUnload += (eventData) => LoaderAssemblyUnloadEvents += 1;

                    int LoaderModuleLoadEvents = 0;
                    int LoaderModuleUnloadEvents = 0;
                    source.Clr.LoaderModuleLoad += (eventData) => LoaderModuleLoadEvents += 1;
                    source.Clr.LoaderModuleUnload += (eventData) => LoaderModuleUnloadEvents += 1;

                    return () => {
                        Logger.logger.Log("Event counts validation");

                        Logger.logger.Log("LoaderAssemblyLoadEvents: " + LoaderAssemblyLoadEvents);
                        Logger.logger.Log("LoaderAssemblyUnloadEvents: " + LoaderAssemblyUnloadEvents);
                        //Unload method just marks as unloadable, not unload immediately, so we check the unload events >=1 to make the tests stable
                        bool LoaderAssemblyResult = LoaderAssemblyLoadEvents >= 100 && LoaderAssemblyUnloadEvents >= 1;
                        Logger.logger.Log("LoaderAssemblyResult check: " + LoaderAssemblyResult);

                        Logger.logger.Log("LoaderModuleLoadEvents: " + LoaderModuleLoadEvents);
                        Logger.logger.Log("LoaderModuleUnloadEvents: " + LoaderModuleUnloadEvents);
                        //Unload method just marks as unloadable, not unload immediately, so we check the unload events >=1 to make the tests stable
                        bool LoaderModuleResult = LoaderModuleLoadEvents >= 100 && LoaderModuleUnloadEvents >= 1;
                        Logger.logger.Log("LoaderModuleResult check: " + LoaderModuleResult);

                        return LoaderAssemblyResult && LoaderModuleResult ? 100 : -1;
                    };
                };
                int ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, providers, 1024, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }
    }
}
