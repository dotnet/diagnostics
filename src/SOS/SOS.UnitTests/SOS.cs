// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.TestHelpers;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825

public static class SOSTestHelpers
{
    public static IEnumerable<object[]> GetConfigurations(string key, string value)
    {
        return TestRunConfiguration.Instance.Configurations.Where((c) => key == null || c.AllSettings.GetValueOrDefault(key) == value).Select(c => new[] { c });
    }

    internal static void SkipIfArm(TestConfiguration config)
    {
        if (config.TargetArchitecture is "arm" or "arm64")
        {
            throw new SkipTestException("SOS does not support ARM architectures");
        }
    }

    internal static async Task RunTest(
        string scriptName,
        SOSRunner.TestInformation information,
        ITestOutputHelper output)
    {
        information.OutputHelper = output;

        if (information.TestLive)
        {
            // Live
            using (SOSRunner runner = await SOSRunner.StartDebugger(information, SOSRunner.DebuggerAction.Live))
            {
                await runner.RunScript(scriptName);
            }
        }

        if (information.TestDump)
        {
            string dumpName = null;

            // Generate a crash dump.
            if (information.DebuggeeDumpOutputRootDir != null)
            {
                dumpName = await SOSRunner.CreateDump(information);
            }

            // Test against a crash dump.
            if (information.DebuggeeDumpInputRootDir != null)
            {
                // With cdb (Windows) or lldb (Linux)
                using (SOSRunner runner = await SOSRunner.StartDebugger(information, SOSRunner.DebuggerAction.LoadDump))
                {
                    await runner.RunScript(scriptName);
                }

                // Using the dotnet-dump analyze tool if the path exists in the config file.
                if (information.TestConfiguration.DotNetDumpPath() != null)
                {
                    // Don't test dotnet-dump on triage dumps when running on desktop CLR.
                    if (information.TestConfiguration.IsNETCore || information.DumpType != SOSRunner.DumpType.Triage)
                    {
                        using (SOSRunner runner = await SOSRunner.StartDebugger(information, SOSRunner.DebuggerAction.LoadDumpWithDotNetDump))
                        {
                            await runner.RunScript(scriptName);
                        }
                    }
                }
            }

            // Test the crash report json file
            if (dumpName != null && information.TestCrashReport)
            {
                TestCrashReport(dumpName, information);
            }
        }
    }

    internal static async Task RunTest(
        TestConfiguration config,
        string debuggeeName,
        string scriptName,
        ITestOutputHelper output,
        string testName = null,
        bool testLive = true,
        bool testDump = true,
        bool testTriage = false)
    {
        await RunTest(scriptName,
            new SOSRunner.TestInformation
            {
                TestConfiguration = config,
                TestName = testName,
                TestLive = testLive,
                TestDump = testDump,
                DebuggeeName = debuggeeName,
                DumpType = SOSRunner.DumpType.Heap
            },
            output);

        // All single-file dumps are currently forced to "full" so skip triage
        // Issue: https://github.com/dotnet/diagnostics/issues/2515
        if (testTriage && !config.PublishSingleFile)
        {
            await RunTest(scriptName,
                new SOSRunner.TestInformation
                {
                    TestConfiguration = config,
                    TestName = testName,
                    TestLive = false,
                    TestDump = testDump,
                    DebuggeeName = debuggeeName,
                    DumpType = SOSRunner.DumpType.Triage
                },
                output);
        }
    }

    internal static void TestCrashReport(string dumpName, SOSRunner.TestInformation information)
    {
        string crashReportPath = dumpName + ".crashreport.json";
        TestRunner.OutputHelper outputHelper = TestRunner.ConfigureLogging(information.TestConfiguration, information.OutputHelper, information.TestName + ".CrashReportTest");
        try
        {
            outputHelper.WriteLine("CrashReportTest for {0}", crashReportPath);
            outputHelper.WriteLine("{");

            AssertX.FileExists("CrashReport", crashReportPath, outputHelper.IndentedOutput);

            dynamic crashReport = JsonConvert.DeserializeObject(File.ReadAllText(crashReportPath));
            Assert.NotNull(crashReport);

            dynamic payload = crashReport.payload;
            Assert.NotNull(payload);
            Version protocol_version = Version.Parse((string)payload.protocol_version);
            Assert.True(protocol_version >= new Version("1.0.0"));
            outputHelper.IndentedOutput.WriteLine($"protocol_version {protocol_version}");

            string process_name = (string)payload.process_name;
            Assert.NotNull(process_name);
            outputHelper.IndentedOutput.WriteLine($"process_name {process_name}");

            Assert.NotNull(payload.threads);
            IEnumerable<dynamic> threads = payload.threads;
            Assert.True(threads.Any());
            outputHelper.IndentedOutput.WriteLine($"threads # {threads.Count()}");

            if (OS.Kind == OSKind.OSX)
            {
                dynamic parameters = crashReport.parameters;
                Assert.NotNull(parameters);
                Assert.NotNull(parameters.ExceptionType);
                Assert.NotNull(parameters.OSVersion);
                Assert.Equal("apple", (string)parameters.SystemManufacturer);
            }
        }
        catch (Exception ex)
        {
            // Log the exception
            outputHelper.IndentedOutput.WriteLine(ex.ToString());
        }
        finally
        {
            outputHelper.WriteLine("}");
            outputHelper.Dispose();
        }
    }
}

[Collection("Windows Dump Generation")]
public class SOS
{
    public SOS(ITestOutputHelper output)
    {
        Output = output;
    }

    private ITestOutputHelper Output { get; set; }

    public static IEnumerable<object[]> Configurations => SOSTestHelpers.GetConfigurations("TestName", value: null);

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task DivZero(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(
            config,
            debuggeeName: "DivZero",
            scriptName: "DivZero.script",
            Output,
            testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task GCTests(TestConfiguration config)
    {
        SOSTestHelpers.SkipIfArm(config);

        // Live only
        await SOSTestHelpers.RunTest(
            config,
            debuggeeName: "GCWhere",
            scriptName: "GCTests.script",
            Output,
            testName: "SOS.GCTests",
            testDump: false);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task GCPOHTests(TestConfiguration config)
    {
        if (config.IsDesktop || config.RuntimeFrameworkVersionMajor < 5)
        {
            throw new SkipTestException("This test validates POH behavior, which was introduced in .net 5");
        }

        await SOSTestHelpers.RunTest(
            config,
            debuggeeName: "GCPOH",
            scriptName: "GCPOH.script",
            Output,
            testName: "SOS.GCPOHTests",
            testDump: false);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task Overflow(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(
            scriptName: "Overflow.script",
            new SOSRunner.TestInformation
            {
                TestConfiguration = config,
                DebuggeeName = "Overflow",
                // Generating the logging for overflow test causes so much output from createdump that it hangs/timesout the test run
                DumpDiagnostics = config.IsNETCore && config.RuntimeFrameworkVersionMajor >= 6,
                // Single file dumps don't capture the overflow exception info so disable testing against a dump
                // Issue: https://github.com/dotnet/diagnostics/issues/2515
                TestDump = !config.PublishSingleFile,
                // The .NET Core createdump facility may not catch stack overflow so use gdb to generate dump
                DumpGenerator = config.StackOverflowCreatesDump ? SOSRunner.DumpGenerator.CreateDump : SOSRunner.DumpGenerator.NativeDebugger
            },
            Output);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task Reflection(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(config, debuggeeName: "ReflectionTest", scriptName: "Reflection.script", Output, testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task SimpleThrow(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(config, debuggeeName: "SimpleThrow", scriptName: "SimpleThrow.script", Output, testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task LineNums(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(
            config,
            debuggeeName: "LineNums",
            scriptName: "LineNums.script",
            Output,
            testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task NestedExceptionTest(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(
            config,
            debuggeeName: "NestedExceptionTest",
            scriptName: "NestedExceptionTest.script",
            Output,
            testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task TaskNestedException(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(
            config,
            debuggeeName: "TaskNestedException",
            scriptName: "TaskNestedException.script",
            Output,
            testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task StackTests(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(
            config,
            debuggeeName: "NestedExceptionTest",
            scriptName: "StackTests.script",
            Output,
            testName: "SOS.StackTests");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task OtherCommands(TestConfiguration config)
    {
        // This debuggee needs the directory of the exes/dlls to load the SymbolTestDll assembly.
        await SOSTestHelpers.RunTest(
            scriptName: "OtherCommands.script",
            new SOSRunner.TestInformation
            {
                TestConfiguration = config,
                TestName = "SOS.OtherCommands",
                DebuggeeName = "SymbolTestApp",
                // Assumes that SymbolTestDll.dll that is dynamically loaded is the parent directory of the single file app
                DebuggeeArguments = config.PublishSingleFile ? Path.Combine("%DEBUG_ROOT%", "..") : "%DEBUG_ROOT%"
            },
            Output);
    }

    [SkippableTheory, MemberData(nameof(SOSTestHelpers.GetConfigurations), "TestName", "SOS.StackAndOtherTests", MemberType = typeof(SOSTestHelpers))]
    public async Task StackAndOtherTests(TestConfiguration config)
    {
        foreach (TestConfiguration currentConfig in TestRunner.EnumeratePdbTypeConfigs(config))
        {
            // Assumes that SymbolTestDll.dll that is dynamically loaded is the parent directory of the single file app
            string debuggeeArguments = currentConfig.PublishSingleFile ? Path.Combine("%DEBUG_ROOT%", "..") : "%DEBUG_ROOT%";
            string debuggeeDumpOutputRootDir = Path.Combine(currentConfig.DebuggeeDumpOutputRootDir(), currentConfig.DebugType);
            string debuggeeDumpInputRootDir = Path.Combine(currentConfig.DebuggeeDumpInputRootDir(), currentConfig.DebugType);

            // This debuggee needs the directory of the exes/dlls to load the SymbolTestDll assembly.
            await SOSTestHelpers.RunTest(
                scriptName: "StackAndOtherTests.script",
                new SOSRunner.TestInformation
                {
                    TestConfiguration = currentConfig,
                    TestName = "SOS.StackAndOtherTests",
                    DebuggeeName = "SymbolTestApp",
                    DebuggeeArguments = debuggeeArguments,
                    DumpNameSuffix = currentConfig.DebugType,
                    DebuggeeDumpOutputRootDir = debuggeeDumpOutputRootDir,
                    DebuggeeDumpInputRootDir = debuggeeDumpInputRootDir,
                },
                Output);

            // This tests using regular Windows PDBs with no managed hosting. SOS should fallback
            // to using native implementations of the host/target/runtime.
            if (currentConfig.DebugType == "full")
            {
                Dictionary<string, string> settings = new(currentConfig.AllSettings)
                {
                    ["SetHostRuntime"] = "-none"
                };
                await SOSTestHelpers.RunTest(
                    scriptName: "StackAndOtherTests.script", new SOSRunner.TestInformation
                    {
                        TestConfiguration = new TestConfiguration(settings),
                        TestName = "SOS.StackAndOtherTests",
                        DebuggeeName = "SymbolTestApp",
                        DebuggeeArguments = debuggeeArguments,
                        DumpNameSuffix = currentConfig.DebugType,
                        DebuggeeDumpOutputRootDir = debuggeeDumpOutputRootDir,
                        DebuggeeDumpInputRootDir = debuggeeDumpInputRootDir,
                    },
                    Output);
            }
        }
    }

    [SkippableTheory, MemberData(nameof(SOSTestHelpers.GetConfigurations), "TestName", "SOS.WebApp3", MemberType = typeof(SOSTestHelpers))]
    public async Task WebApp3(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest("WebApp.script", new SOSRunner.TestInformation
        {
            TestConfiguration = config,
            TestLive = false,
            DebuggeeName = "WebApp3",
            UsePipeSync = true,
            DumpGenerator = SOSRunner.DumpGenerator.DotNetDump
        },
                Output);
    }

    [SkippableTheory, MemberData(nameof(SOSTestHelpers.GetConfigurations), "TestName", "SOS.DualRuntimes", MemberType = typeof(SOSTestHelpers))]
    public async Task DualRuntimes(TestConfiguration config)
    {
        if (config.PublishSingleFile)
        {
            throw new SkipTestException("Single file not supported");
        }
        // The assembly path, class and function name of the desktop test code to load/run
        string desktopTestParameters = TestConfiguration.MakeCanonicalPath(config.GetValue("DesktopTestParameters"));
        if (string.IsNullOrEmpty(desktopTestParameters))
        {
            throw new SkipTestException("DesktopTestParameters config value does not exists");
        }
        await SOSTestHelpers.RunTest(
            scriptName: "DualRuntimes.script",
            new SOSRunner.TestInformation
            {
                TestConfiguration = config,
                TestLive = false,
                TestName = "SOS.DualRuntimes",
                DebuggeeName = "WebApp3",
                DebuggeeArguments = desktopTestParameters,
                UsePipeSync = true,
                DumpGenerator = SOSRunner.DumpGenerator.DotNetDump
            },
            Output);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task ConcurrentDictionaries(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(
            scriptName: "ConcurrentDictionaries.script",
            new SOSRunner.TestInformation
            {
                TestConfiguration = config,
                TestLive = false,
                DebuggeeName = "DotnetDumpCommands",
                DebuggeeArguments = "dcd",
                DumpNameSuffix = "dcd",
                UsePipeSync = true,
                DumpGenerator = SOSRunner.DumpGenerator.DotNetDump,
            },
            Output);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task DumpGen(TestConfiguration config)
    {
        await SOSTestHelpers.RunTest(
            scriptName: "DumpGen.script",
            new SOSRunner.TestInformation
            {
                TestConfiguration = config,
                TestLive = false,
                DebuggeeName = "DotnetDumpCommands",
                DebuggeeArguments = "dumpgen",
                DumpNameSuffix = "dumpgen",
                UsePipeSync = true,
                DumpGenerator = SOSRunner.DumpGenerator.DotNetDump,
            },
            Output);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task LLDBPluginTests(TestConfiguration config)
    {
        SOSTestHelpers.SkipIfArm(config);

        if (OS.Kind == OSKind.Windows || config.IsDesktop || config.RuntimeFrameworkVersionMajor == 1 || OS.IsAlpine)
        {
            throw new SkipTestException("lldb plugin tests not supported on Windows, Alpine Linux or .NET Core 1.1");
        }

        string testName = "SOS." + nameof(LLDBPluginTests);
        TestRunner.OutputHelper outputHelper = null;
        try
        {
            // Setup the logging from the options in the config file
            outputHelper = TestRunner.ConfigureLogging(config, Output, testName);

            outputHelper.WriteLine("Starting {0}", testName);
            outputHelper.WriteLine("{");

            string program;
            StringBuilder arguments = new();
            if (OS.Kind == OSKind.OSX)
            {
                program = "xcrun";
                arguments.Append("python3 ");
            }
            else
            {
                // We should verify what python version this is. 2.7 is out of
                // support for a while now, but we have old OS's.
                program = "/usr/bin/python";
                if (!File.Exists(program))
                {
                    throw new ArgumentException($"{program} does not exists");
                }
            }
            string repoRootDir = TestConfiguration.MakeCanonicalPath(config.AllSettings["RepoRootDir"]);

            // Get test python script path
            string scriptDir = Path.Combine(repoRootDir, "src", "SOS", "lldbplugin.tests");
            arguments.Append(Path.Combine(scriptDir, "test_libsosplugin.py"));
            arguments.Append(' ');

            // Get lldb path
            arguments.AppendFormat("--lldb {0} ", Environment.GetEnvironmentVariable("LLDB_PATH") ?? throw new ArgumentException("LLDB_PATH environment variable not set"));

            // Add dotnet host program and arguments
            arguments.Append("--host \"");
            arguments.Append(config.HostExe);
            arguments.Append(' ');
            if (!string.IsNullOrWhiteSpace(config.HostArgs))
            {
                arguments.Append(config.HostArgs);
                arguments.Append(' ');
            }
            arguments.Append("\" ");

            // Add lldb plugin path
            arguments.AppendFormat("--plugin {0} ", config.SOSPath() ?? throw new ArgumentException("SOSPath config not set"));

            // Add log directory
            string logFileDir = Path.Combine(config.LogDirPath, config.RuntimeFrameworkVersion);
            Directory.CreateDirectory(logFileDir);
            arguments.AppendFormat("--logfiledir {0} ", logFileDir);

            // Add test debuggee assembly
            string testDebuggee = Path.Combine(repoRootDir, "artifacts", "bin", "TestDebuggee", config.TargetConfiguration, config.BuildProjectFramework, "TestDebuggee.dll");
            arguments.AppendFormat("--assembly {0}", testDebuggee);

            // Create the python script process runner
            ProcessRunner processRunner = new ProcessRunner(program, arguments.ToString()).
                WithEnvironmentVariable("DOTNET_ROOT", config.DotNetRoot).
                WithLog(new TestRunner.TestLogger(outputHelper.IndentedOutput)).
                WithTimeout(TimeSpan.FromMinutes(10)).
                WithExpectedExitCode(0).
                WithWorkingDirectory(scriptDir).
                // Turn on stress logging so the dumplog and histinit commands pass
                WithEnvironmentVariable("DOTNET_LogFacility", "0xffffffbf").
                WithEnvironmentVariable("DOTNET_LogLevel", "6").
                WithEnvironmentVariable("DOTNET_StressLog", "1").
                WithEnvironmentVariable("DOTNET_StressLogSize", "65536");

            // Start the process runner
            processRunner.Start();

            // Wait for the debuggee to finish
            await processRunner.WaitForExit();
        }
        catch (Exception ex)
        {
            // Log the exception
            outputHelper?.WriteLine(ex.ToString());
            throw;
        }
        finally
        {
            outputHelper?.WriteLine("}");
            outputHelper?.Dispose();
        }
    }
}
