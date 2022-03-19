// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.Diagnostics.TestHelpers;
using SOS.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;

namespace Microsoft.Diagnostics
{
    public class DbgShimTests : IDisposable
    {
        private const string ListenerName = "DbgShimTests";

        public static IEnumerable<object[]> GetConfigurations(string key, string value)
        {
            return TestRunConfiguration.Instance.Configurations.Where((c) => key == null || c.AllSettings.GetValueOrDefault(key) == value).Select(c => new[] { c });
        }

        public static IEnumerable<object[]> Configurations => GetConfigurations("TestName", null);

        private ITestOutputHelper Output { get; }

        public DbgShimTests(ITestOutputHelper output)
        {
            Output = output;
            LoggingListener.EnableListener(output, ListenerName);
        }

        void IDisposable.Dispose() => Trace.Listeners.Remove(ListenerName);

        /// <summary>
        /// Test RegisterForRuntimeStartup for launch
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task Launch1(TestConfiguration config)
        {
            await RemoteInvoke(config, nameof(Launch1), static async (string configXml) =>
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: true);
                TestRegisterForRuntimeStartup(debuggeeInfo, 1);

                // Once the debuggee is resumed now wait until it starts
                Assert.True(await debuggeeInfo.WaitForDebuggee());
                return 0;
            });
        }

        /// <summary>
        /// Test RegisterForRuntimeStartupEx for launch
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task Launch2(TestConfiguration config)
        {
            await RemoteInvoke(config, nameof(Launch2), static async (string configXml) =>
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: true);
                TestRegisterForRuntimeStartup(debuggeeInfo, 2);

                // Once the debuggee is resumed now wait until it starts
                Assert.True(await debuggeeInfo.WaitForDebuggee());
                return 0;
            });
        }

        /// <summary>
        /// Test RegisterForRuntimeStartup3 for launch
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task Launch3(TestConfiguration config)
        {
            DbgShimAPI.Initialize(config.DbgShimPath());
            if (!DbgShimAPI.IsRegisterForRuntimeStartup3Supported)
            {
                throw new SkipTestException("IsRegisterForRuntimeStartup3 not supported");
            }
            await RemoteInvoke(config, nameof(Launch3), static async (string configXml) => 
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: true);
                TestRegisterForRuntimeStartup(debuggeeInfo, 3);

                // Once the debuggee is resumed now wait until it starts
                Assert.True(await debuggeeInfo.WaitForDebuggee());
                return 0;
            });
        }

        /// <summary>
        /// Test RegisterForRuntimeStartup for attach 
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task Attach1(TestConfiguration config)
        {
            await RemoteInvoke(config, nameof(Attach1), static async (string configXml) => 
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: false);
                TestRegisterForRuntimeStartup(debuggeeInfo, 1);
                return 0;
            });
        }

        /// <summary>
        /// Test RegisterForRuntimeStartupEx for attach 
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task Attach2(TestConfiguration config)
        {
            await RemoteInvoke(config, nameof(Attach2), static async (string configXml) => 
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: false);
                TestRegisterForRuntimeStartup(debuggeeInfo, 2);
                return 0;
            });
        }

        /// <summary>
        /// Test RegisterForRuntimeStartup3 for attach
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task Attach3(TestConfiguration config)
        {
            DbgShimAPI.Initialize(config.DbgShimPath());
            if (!DbgShimAPI.IsRegisterForRuntimeStartup3Supported)
            {
                throw new SkipTestException("IsRegisterForRuntimeStartup3 not supported");
            }
            await RemoteInvoke(config, nameof(Attach3), static async (string configXml) => 
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: false);
                TestRegisterForRuntimeStartup(debuggeeInfo, 3);
                return 0;
            });
        }

        /// <summary>
        /// Test EnumerateCLRs/CloseCLREnumeration
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task EnumerateCLRs(TestConfiguration config)
        {
            await RemoteInvoke(config, nameof(EnumerateCLRs), static async (string configXml) =>
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: false);
                Trace.TraceInformation("EnumerateCLRs pid {0} START", debuggeeInfo.ProcessId);
                HResult hr = DbgShimAPI.EnumerateCLRs(debuggeeInfo.ProcessId, (IntPtr[] continueEventHandles, string[] moduleNames) =>
                {
                    Assert.Single(continueEventHandles);
                    Assert.Single(moduleNames);
                    for (int i = 0; i < continueEventHandles.Length; i++)
                    {
                        Trace.TraceInformation("EnumerateCLRs pid {0} {1:X16} {2}", debuggeeInfo.ProcessId, continueEventHandles[i].ToInt64(), moduleNames[i]);
                        AssertX.FileExists("ModuleFilePath", moduleNames[i], debuggeeInfo.Output);
                    }
                });
                AssertResult(hr);
                Trace.TraceInformation("EnumerateCLRs pid {0} DONE", debuggeeInfo.ProcessId);
                return 0;
            });
        }

        /// <summary>
        /// Test CreateVersionStringFromModule/CreateDebuggingInterfaceFromVersion
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task CreateDebuggingInterfaceFromVersion(TestConfiguration config)
        {
            await RemoteInvoke(config, nameof(CreateDebuggingInterfaceFromVersion), static async (string configXml) =>
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: false);
                TestCreateDebuggingInterface(debuggeeInfo, 0);
                return 0;
            });
        }

        /// <summary>
        /// Test CreateVersionStringFromModule/CreateDebuggingInterfaceFromVersionEx
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task CreateDebuggingInterfaceFromVersionEx(TestConfiguration config)
        {
            await RemoteInvoke(config, nameof(CreateDebuggingInterfaceFromVersionEx), static async (string configXml) =>
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: false);
                TestCreateDebuggingInterface(debuggeeInfo, 1);
                return 0;
            });
        }

        /// <summary>
        /// Test CreateVersionStringFromModule/CreateDebuggingInterfaceFromVersion2
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task CreateDebuggingInterfaceFromVersion2(TestConfiguration config)
        {
            await RemoteInvoke(config, nameof(CreateDebuggingInterfaceFromVersion2), static async (string configXml) =>
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: false);
                TestCreateDebuggingInterface(debuggeeInfo, 2);
                return 0;
            });
        }

        /// <summary>
        /// Test CreateVersionStringFromModule/CreateDebuggingInterfaceFromVersion3
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task CreateDebuggingInterfaceFromVersion3(TestConfiguration config)
        {
            DbgShimAPI.Initialize(config.DbgShimPath());
            if (!DbgShimAPI.IsCreateDebuggingInterfaceFromVersion3Supported)
            {
                throw new SkipTestException("CreateDebuggingInterfaceFromVersion3 not supported");
            }
            await RemoteInvoke(config, nameof(CreateDebuggingInterfaceFromVersion3), static async (string configXml) =>
            {
                using DebuggeeInfo debuggeeInfo = await StartDebuggee(configXml, launch: false);
                TestCreateDebuggingInterface(debuggeeInfo, 3);
                return 0;
            });
        }

        [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "OpenVirtualProcess")]
        public async Task OpenVirtualProcess(TestConfiguration config)
        {
            if (!config.AllSettings.ContainsKey("DumpFile"))
            {
                throw new SkipTestException("OpenVirtualProcessTest: No dump file");
            }
            await RemoteInvoke(config, nameof(OpenVirtualProcess), static (string configXml) =>
            {
                AfterInvoke(configXml, out TestConfiguration cfg, out ITestOutputHelper output);

                DbgShimAPI.Initialize(cfg.DbgShimPath());
                AssertResult(DbgShimAPI.CLRCreateInstance(out ICLRDebugging clrDebugging));
                Assert.NotNull(clrDebugging);

                TestDump testDump = new(cfg);
                ITarget target = testDump.Target;
                IRuntimeService runtimeService = target.Services.GetService<IRuntimeService>();
                IRuntime runtime = runtimeService.EnumerateRuntimes().Single();

                CorDebugDataTargetWrapper dataTarget = new(target.Services);
                LibraryProviderWrapper libraryProvider = new(target.OperatingSystem, runtime.RuntimeModule.BuildId, runtime.GetDbiFilePath(), runtime.GetDacFilePath());
                ClrDebuggingVersion maxDebuggerSupportedVersion = new()
                {
                    StructVersion = 0,
                    Major = 4,
                    Minor = 0,
                    Build = 0,
                    Revision = 0,
                };
                HResult hr = clrDebugging.OpenVirtualProcess(
                    runtime.RuntimeModule.ImageBase,
                    dataTarget.ICorDebugDataTarget,
                    libraryProvider.ILibraryProvider,
                    maxDebuggerSupportedVersion,
                    in RuntimeWrapper.IID_ICorDebugProcess,
                    out IntPtr corDebugProcess,
                    out ClrDebuggingVersion version,
                    out ClrDebuggingProcessFlags flags);

                AssertResult(hr);
                Assert.NotEqual(IntPtr.Zero, corDebugProcess);
                Assert.Equal(1, COMHelper.Release(corDebugProcess));
                Assert.Equal(0, COMHelper.Release(corDebugProcess));
                Assert.Equal(0, clrDebugging.Release());
                return Task.FromResult(0);
            });
        }

        #region Helper functions

        private static async Task<DebuggeeInfo> StartDebuggee(string configXml, bool launch)
        {
            AfterInvoke(configXml, out TestConfiguration config, out ITestOutputHelper output);

            DebuggeeInfo debuggeeInfo = new(output, config, launch);
            string debuggeeName = config.DebuggeeName();

            Assert.NotNull(debuggeeName);
            Assert.NotNull(config.DbgShimPath());

            DbgShimAPI.Initialize(config.DbgShimPath());

            // Restore and build the debuggee
            DebuggeeConfiguration debuggeeConfig = await DebuggeeCompiler.Execute(config, debuggeeName, debuggeeInfo.Output);

            // Build the debuggee command line
            StringBuilder commandLine = new();

            // Get the full launch command line (includes the host if required)
            if (!string.IsNullOrWhiteSpace(config.HostExe))
            {
                commandLine.Append(config.HostExe);
                commandLine.Append(" ");
                if (!string.IsNullOrWhiteSpace(config.HostArgs))
                {
                    commandLine.Append(config.HostArgs);
                    commandLine.Append(" ");
                }
            }
            commandLine.Append(debuggeeConfig.BinaryExePath);
            commandLine.Append(" ");
            commandLine.Append(debuggeeInfo.PipeName);

            Trace.TraceInformation("CreateProcessForLaunch {0} {1} {2}", launch, commandLine.ToString(), debuggeeInfo.PipeName);
            AssertResult(DbgShimAPI.CreateProcessForLaunch(commandLine.ToString(), launch, currentDirectory: null, out int processId, out IntPtr resumeHandle));
            Assert.NotEqual(IntPtr.Zero, resumeHandle);
            Trace.TraceInformation("CreateProcessForLaunch pid {0} {1}", processId, commandLine.ToString());

            debuggeeInfo.ResumeHandle = resumeHandle;
            debuggeeInfo.SetProcessId(processId);

            // Wait for debuggee to start if attach/run
            if (!launch)
            {
                Assert.True(await debuggeeInfo.WaitForDebuggee());
            }
            Trace.TraceInformation("CreateProcessForLaunch pid {0} DONE", processId);
            return debuggeeInfo;
        }

        private static void TestRegisterForRuntimeStartup(DebuggeeInfo debuggeeInfo, int api)
        {
            TestConfiguration config = debuggeeInfo.TestConfiguration;
            AutoResetEvent wait = new AutoResetEvent(false);
            string applicationGroupId =  null;
            IntPtr unregisterToken = IntPtr.Zero;
            HResult result = HResult.S_OK;
            HResult callbackResult = HResult.S_OK;
            Exception callbackException = null;
            ICorDebug corDebug = null;

            Trace.TraceInformation("RegisterForRuntimeStartup pid {0} launch {1} api {2} START", debuggeeInfo.ProcessId, debuggeeInfo.Launch, api);

            DbgShimAPI.RuntimeStartupCallbackDelegate callback = (ICorDebug cordbg, object parameter, HResult hr) => {
                Trace.TraceInformation("RegisterForRuntimeStartup in callback pid {0} hr {1:X}", debuggeeInfo.ProcessId, hr);
                corDebug = cordbg;
                callbackResult = hr;
                try
                {
                    // Only check the ICorDebug instance if success
                    if (hr)
                    {
                        TestICorDebug(debuggeeInfo, cordbg);
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                    callbackException = ex;
                }
                wait.Set();
            };

            switch (api)
            {
                case 1:
                    result = DbgShimAPI.RegisterForRuntimeStartup(debuggeeInfo.ProcessId, parameter: IntPtr.Zero, out unregisterToken, callback);
                    break;
                case 2:
                    result = DbgShimAPI.RegisterForRuntimeStartupEx(debuggeeInfo.ProcessId, applicationGroupId, parameter: IntPtr.Zero, out unregisterToken, callback);
                    break;
                case 3:
                    LibraryProviderWrapper libraryProvider = new(config.RuntimeModulePath(), config.DbiModulePath(), config.DacModulePath());
                    result = DbgShimAPI.RegisterForRuntimeStartup3(debuggeeInfo.ProcessId, applicationGroupId, parameter: IntPtr.Zero, libraryProvider.ILibraryProvider, out unregisterToken, callback);
                    break;
                default:
                    throw new ArgumentException(nameof(api));
            }

            if (debuggeeInfo.Launch) 
            {
                AssertResult(debuggeeInfo.ResumeDebuggee());
            }

            AssertResult(result);

            Trace.TraceInformation("RegisterForRuntimeStartup pid {0} waiting for callback", debuggeeInfo.ProcessId);
            Assert.True(wait.WaitOne(TimeSpan.FromMinutes(3)));
            Trace.TraceInformation("RegisterForRuntimeStartup pid {0} after callback wait", debuggeeInfo.ProcessId);
            
            AssertResult(DbgShimAPI.UnregisterForRuntimeStartup(unregisterToken));
            Assert.Null(callbackException);

            switch (api)
            {
                case 1:
                case 2:
                    // The old APIs fail on single file apps
                    Assert.Equal(!debuggeeInfo.TestConfiguration.PublishSingleFile, callbackResult);
                    break;
                case 3:
                    // The new API should always succeed
                    AssertResult(callbackResult);
                    break;
            }

            if (callbackResult)
            {
                AssertResult(debuggeeInfo.WaitForCreateProcess());
                Assert.Equal(0, corDebug.Release());
            }
            else
            {
                debuggeeInfo.Kill();
            }

            Trace.TraceInformation("RegisterForRuntimeStartup pid {0} DONE", debuggeeInfo.ProcessId);
        }

        private static void TestCreateDebuggingInterface(DebuggeeInfo debuggeeInfo, int api)
        {
            Trace.TraceInformation("TestCreateDebuggingInterface pid {0} api {1} START", debuggeeInfo.ProcessId, api);
            HResult hr = DbgShimAPI.EnumerateCLRs(debuggeeInfo.ProcessId, (IntPtr[] continueEventHandles, string[] moduleNames) =>
            {
                TestConfiguration config = debuggeeInfo.TestConfiguration;
                Assert.Single(continueEventHandles);
                Assert.Single(moduleNames);
                for (int i = 0; i < continueEventHandles.Length; i++)
                {
                    Trace.TraceInformation("TestCreateDebuggingInterface pid {0} {1:X16} {2}", debuggeeInfo.ProcessId, continueEventHandles[i].ToInt64(), moduleNames[i]);
                    AssertX.FileExists("ModuleFilePath", moduleNames[i], debuggeeInfo.Output);

                    AssertResult(DbgShimAPI.CreateVersionStringFromModule(debuggeeInfo.ProcessId, moduleNames[i], out string versionString));
                    Trace.TraceInformation("TestCreateDebuggingInterface pid {0} version string {1}", debuggeeInfo.ProcessId, versionString);
                    Assert.False(string.IsNullOrWhiteSpace(versionString));

                    ICorDebug corDebug = null;
                    string applicationGroupId = null;
                    HResult result;
                    switch (api)
                    {
                        case 0:
                            result = DbgShimAPI.CreateDebuggingInterfaceFromVersion(versionString, out corDebug);
                            break;
                        case 1:
                            result = DbgShimAPI.CreateDebuggingInterfaceFromVersionEx(DbgShimAPI.CorDebugVersion_4_0, versionString, out corDebug);
                            break;
                        case 2:
                            result = DbgShimAPI.CreateDebuggingInterfaceFromVersion2(DbgShimAPI.CorDebugVersion_4_0, versionString, applicationGroupId, out corDebug);
                            break;
                        case 3:
                            LibraryProviderWrapper libraryProvider = new(config.RuntimeModulePath(), config.DbiModulePath(), config.DacModulePath());
                            result = DbgShimAPI.CreateDebuggingInterfaceFromVersion3(DbgShimAPI.CorDebugVersion_4_0, versionString, applicationGroupId, libraryProvider.ILibraryProvider, out corDebug);
                            break;
                        default:
                            throw new ArgumentException(nameof(api));
                    }

                    Trace.TraceInformation("TestCreateDebuggingInterface pid {0} after API {1} call", debuggeeInfo.ProcessId, api);

                    switch (api)
                    {
                        case 0:
                        case 1:
                        case 2:
                            // The old APIs fail on single file apps
                            Assert.Equal(!debuggeeInfo.TestConfiguration.PublishSingleFile, result);
                            break;
                        case 3:
                            // The new API should always succeed
                            AssertResult(result);
                            break;
                    }

                    if (result)
                    {
                        TestICorDebug(debuggeeInfo, corDebug);
                        AssertResult(debuggeeInfo.WaitForCreateProcess());
                        Assert.Equal(0, corDebug.Release());
                    }
                    else
                    {
                        Trace.TraceInformation("TestCreateDebuggingInterface pid {0} FAILED {1}", debuggeeInfo.ProcessId, result);
                    }
                }
            });
            AssertResult(hr);
            Trace.TraceInformation("TestCreateDebuggingInterface pid {0} DONE", debuggeeInfo.ProcessId);
        }

        private static readonly Guid IID_ICorDebugProcess = new Guid("3D6F5F64-7538-11D3-8D5B-00104B35E7EF");

        private static void TestICorDebug(DebuggeeInfo debuggeeInfo, ICorDebug corDebug)
        {
            Assert.NotNull(corDebug);
            AssertResult(corDebug.Initialize());
            ManagedCallbackWrapper managedCallback = new(debuggeeInfo);
            AssertResult(corDebug.SetManagedHandler(managedCallback.ICorDebugManagedCallback));
            Trace.TraceInformation("TestICorDebug pid before DebugActiveProcess {0}", debuggeeInfo.ProcessId);
            AssertResult(corDebug.DebugActiveProcess(debuggeeInfo.ProcessId, out IntPtr process));
            Trace.TraceInformation("TestICorDebug pid after DebugActiveProcess {0}", debuggeeInfo.ProcessId);
            AssertResult(COMHelper.QueryInterface(process, IID_ICorDebugProcess, out IntPtr icdp));
            Assert.True(icdp != IntPtr.Zero);
            COMHelper.Release(icdp);
        }

        /// <summary>
        /// The reason we are running each test in it's own process using the remote executor is that the DBI/DAC are
        /// never unloaded (and the existing dbgshim interfaces don't allow a way to do this). This is a problem on
        /// Linux/MacOS to have multiple DAC loaded because DBI references the DAC's PAL exports and the loader gets
        /// confused which DAC should be used for which DBI. This isn't a problem on Windows since there is no PAL.
        /// </summary>
        /// <param name="config">test configuration</param>
        /// <param name="testName">name of test for the log file</param>
        /// <param name="method">delegate to call in the remote process</param>
        private async Task RemoteInvoke(TestConfiguration config, string testName, Func<string, Task<int>> method)
        {
            string singlefile = config.PublishSingleFile ? ".SingleFile" : "";
            testName = $"DbgShim.UnitTests{singlefile}.{testName}";
            string dumpPath = Path.Combine(config.LogDirPath, testName + ".dmp");
            using TestRunner.OutputHelper output = TestRunner.ConfigureLogging(config, Output, testName);
            int exitCode = await RemoteExecutorHelper.RemoteInvoke(output, config, TimeSpan.FromMinutes(6), dumpPath, method);
            Assert.Equal(0, exitCode);
        }

        /// <summary>
        /// Used in the remote invoke delegate to deserialize the test configuration xml and setup test output logging.
        /// </summary>
        /// <param name="config">test configuration xml</param>
        /// <param name="config">test configuration instance</param>
        /// <param name="output">test output instance</param>
        private static void AfterInvoke(string configXml, out TestConfiguration config, out ITestOutputHelper output)
        {
            config = TestConfiguration.Deserialize(configXml);
            output = new ConsoleTestOutputHelper();
            LoggingListener.EnableListener(output, ListenerName);
        }

        private static void AssertResult(HResult hr)
        {
            Assert.Equal<HResult>(HResult.S_OK, hr);
        }

        #endregion
    }

    public static class DbgShimTestExtensions
    {
        public static string DbgShimPath(this TestConfiguration config)
        {
            return TestConfiguration.MakeCanonicalPath(config.GetValue("DbgShimPath"));
        }

        public static string RuntimeModulePath(this TestConfiguration config)
        {
            return TestConfiguration.MakeCanonicalPath(config.GetValue("RuntimeModulePath"));
        }

        public static string DbiModulePath(this TestConfiguration config)
        {
            return TestConfiguration.MakeCanonicalPath(config.GetValue("DbiModulePath"));
        }

        public static string DacModulePath(this TestConfiguration config)
        {
            return TestConfiguration.MakeCanonicalPath(config.GetValue("DacModulePath"));
        }

        public static string DebuggeeName(this TestConfiguration config)
        {
            return config.GetValue("DebuggeeName");
        }
    }
}
