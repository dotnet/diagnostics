// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.TestHelpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;

public class SOS
{
    public SOS(ITestOutputHelper output)
    {
        Output = output;
    }

    ITestOutputHelper Output { get; set; }

    public static IEnumerable<object[]> GetConfigurations(string key, string value)
    {
        return TestRunConfiguration.Instance.Configurations.Where((c) => key == null || c.AllSettings.GetValueOrDefault(key) == value).Select(c => new[] { c });
    }

    public static IEnumerable<object[]> Configurations => GetConfigurations("TestName", null);

    private void SkipIfArm(TestConfiguration config)
    {
        if (config.TargetArchitecture == "arm" || config.TargetArchitecture == "arm64")
        {
            throw new SkipTestException("SOS does not support ARM architectures");
        }
    }

    private async Task RunTest(string scriptName, SOSRunner.TestInformation information)
    {
        information.OutputHelper = Output;

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
                // TODO: dotnet-dump currently doesn't support macho core dumps that the MacOS createdump generates
                if (information.TestConfiguration.DotNetDumpPath() != null && OS.Kind != OSKind.OSX)
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

    private void TestCrashReport(string dumpName, SOSRunner.TestInformation information)
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
                Assert.Equal(parameters.SystemManufacturer, "apple");
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

    private async Task RunTest(TestConfiguration config, string debuggeeName, string scriptName, string testName = null, bool testLive = true, bool testDump = true, bool testTriage = false)
    {
        await RunTest(scriptName, new SOSRunner.TestInformation {
            TestConfiguration = config,
            TestName = testName,
            TestLive = testLive,
            TestDump = testDump,
            DebuggeeName = debuggeeName,
            DumpType = SOSRunner.DumpType.Heap
        });

        // All single-file dumps are currently forced to "full" so skip triage
        // Issue: https://github.com/dotnet/diagnostics/issues/2515
        if (testTriage && !config.PublishSingleFile)
        {
            await RunTest(scriptName, new SOSRunner.TestInformation {
                TestConfiguration = config,
                TestName = testName,
                TestLive = false,
                TestDump = testDump,
                DebuggeeName = debuggeeName,
                DumpType = SOSRunner.DumpType.Triage
            });
        }
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task DivZero(TestConfiguration config)
    {
        await RunTest(config, "DivZero", "DivZero.script", testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task GCTests(TestConfiguration config)
    {
        SkipIfArm(config);

        // Live only
        await RunTest(config, "GCWhere", "GCTests.script", testName: "SOS.GCTests", testDump: false);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task GCPOHTests(TestConfiguration config)
    {
        if (config.IsDesktop || config.RuntimeFrameworkVersionMajor < 5)
        {
            throw new SkipTestException("This test validates POH behavior, which was introduced in .net 5");
        }
        await RunTest(config, "GCPOH", "GCPOH.script", testName: "SOS.GCPOHTests", testDump: false);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task Overflow(TestConfiguration config)
    {
        await RunTest("Overflow.script", new SOSRunner.TestInformation {
            TestConfiguration = config,
            DebuggeeName = "Overflow",
            // Generating the logging for overflow test causes so much output from createdump that it hangs/timesout the test run
            DumpDiagnostics = config.IsNETCore && config.RuntimeFrameworkVersionMajor >= 6,
            // Single file dumps don't capture the overflow exception info so disable testing against a dump
            // Issue: https://github.com/dotnet/diagnostics/issues/2515
            TestDump = config.PublishSingleFile ? false : true,
            // The .NET Core createdump facility may not catch stack overflow so use gdb to generate dump
            DumpGenerator = config.StackOverflowCreatesDump ? SOSRunner.DumpGenerator.CreateDump : SOSRunner.DumpGenerator.NativeDebugger
        });
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task Reflection(TestConfiguration config)
    {
        await RunTest(config, "ReflectionTest", "Reflection.script", testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task SimpleThrow(TestConfiguration config)
    {
        await RunTest(config, "SimpleThrow", "SimpleThrow.script", testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task LineNums(TestConfiguration config)
    {
        await RunTest(config, "LineNums", "LineNums.script", testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task NestedExceptionTest(TestConfiguration config)
    {
        await RunTest(config, "NestedExceptionTest", "NestedExceptionTest.script", testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task TaskNestedException(TestConfiguration config)
    {
        await RunTest(config, "TaskNestedException", "TaskNestedException.script", testTriage: true);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task StackTests(TestConfiguration config)
    {
        await RunTest(config, "NestedExceptionTest", "StackTests.script", testName: "SOS.StackTests");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task OtherCommands(TestConfiguration config)
    {
        // This debuggee needs the directory of the exes/dlls to load the SymbolTestDll assembly.
        await RunTest("OtherCommands.script", new SOSRunner.TestInformation {
            TestConfiguration = config,
            TestName = "SOS.OtherCommands",
            DebuggeeName = "SymbolTestApp",
            // Assumes that SymbolTestDll.dll that is dynamically loaded is the parent directory of the single file app
            DebuggeeArguments = config.PublishSingleFile ? Path.Combine("%DEBUG_ROOT%", "..") : "%DEBUG_ROOT%"
        });
    }

    [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "SOS.StackAndOtherTests")]
    public async Task StackAndOtherTests(TestConfiguration config)
    {
        foreach (TestConfiguration currentConfig in TestRunner.EnumeratePdbTypeConfigs(config))
        {
            // Assumes that SymbolTestDll.dll that is dynamically loaded is the parent directory of the single file app
            string debuggeeArguments = currentConfig.PublishSingleFile ? Path.Combine("%DEBUG_ROOT%", "..") : "%DEBUG_ROOT%";
            string debuggeeDumpOutputRootDir = Path.Combine(currentConfig.DebuggeeDumpOutputRootDir(), currentConfig.DebugType);
            string debuggeeDumpInputRootDir = Path.Combine(currentConfig.DebuggeeDumpInputRootDir(), currentConfig.DebugType);

            // This debuggee needs the directory of the exes/dlls to load the SymbolTestDll assembly.
            await RunTest("StackAndOtherTests.script", new SOSRunner.TestInformation {
                TestConfiguration = currentConfig,
                TestName = "SOS.StackAndOtherTests",
                DebuggeeName = "SymbolTestApp",
                DebuggeeArguments = debuggeeArguments,
                DumpNameSuffix = currentConfig.DebugType,
                DebuggeeDumpOutputRootDir = debuggeeDumpOutputRootDir,
                DebuggeeDumpInputRootDir = debuggeeDumpInputRootDir,
            });

            // This tests using regular Windows PDBs with no managed hosting. SOS should fallback 
            // to using native implementations of the host/target/runtime.
            if (currentConfig.DebugType == "full")
            {
                var settings = new Dictionary<string, string>(currentConfig.AllSettings);

                // Currently the C++ runtime enumeration fallback doesn't support single file on Windows
                // Issue: https://github.com/dotnet/diagnostics/issues/2515
                if (!(OS.Kind == OSKind.Windows && currentConfig.PublishSingleFile))
                {
                    settings["SetHostRuntime"] = "-none";
                }
                await RunTest("StackAndOtherTests.script", new SOSRunner.TestInformation {
                    TestConfiguration = new TestConfiguration(settings),
                    TestName = "SOS.StackAndOtherTests",
                    DebuggeeName = "SymbolTestApp",
                    DebuggeeArguments = debuggeeArguments,
                    DumpNameSuffix = currentConfig.DebugType,
                    DebuggeeDumpOutputRootDir = debuggeeDumpOutputRootDir,
                    DebuggeeDumpInputRootDir = debuggeeDumpInputRootDir,
                });
            }
        }
    }

    [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "SOS.WebApp3")]
    public async Task WebApp3(TestConfiguration config)
    {
        await RunTest("WebApp.script", new SOSRunner.TestInformation {
            TestConfiguration = config,
            TestLive = false,
            DebuggeeName = "WebApp3",
            UsePipeSync = true,
            DumpGenerator = SOSRunner.DumpGenerator.DotNetDump
        });
    }

    [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "SOS.DualRuntimes")]
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
        await RunTest("DualRuntimes.script", new SOSRunner.TestInformation {
            TestConfiguration = config,
            TestLive = false,
            TestName = "SOS.DualRuntimes",
            DebuggeeName = "WebApp3",
            DebuggeeArguments = desktopTestParameters,
            UsePipeSync = true,
            DumpGenerator = SOSRunner.DumpGenerator.DotNetDump
        });
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task ConcurrentDictionaries(TestConfiguration config)
    {
        await RunTest("ConcurrentDictionaries.script", new SOSRunner.TestInformation {
            TestConfiguration = config,
            TestLive = false,
            DebuggeeName = "DotnetDumpCommands",
            DebuggeeArguments = "dcd",
            DumpNameSuffix = "dcd",
            UsePipeSync = true,
            DumpGenerator = SOSRunner.DumpGenerator.DotNetDump,
        });
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task DumpGen(TestConfiguration config)
    {
        await RunTest("DumpGen.script", new SOSRunner.TestInformation {
            TestConfiguration = config,
            TestLive = false,
            DebuggeeName = "DotnetDumpCommands",
            DebuggeeArguments = "dumpgen",
            DumpNameSuffix = "dumpgen",
            UsePipeSync = true,
            DumpGenerator = SOSRunner.DumpGenerator.DotNetDump,
        });
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task LLDBPluginTests(TestConfiguration config)
    {
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

            string program = "/usr/bin/python";
            if (!File.Exists(program))
            {
                throw new ArgumentException($"{program} does not exists");
            }
            var arguments = new StringBuilder();
            string repoRootDir = TestConfiguration.MakeCanonicalPath(config.AllSettings["RepoRootDir"]);

            // Get test python script path
            string scriptDir = Path.Combine(repoRootDir, "src", "SOS", "lldbplugin.tests");
            arguments.Append(Path.Combine(scriptDir, "test_libsosplugin.py"));
            arguments.Append(" ");

            // Get lldb path
            arguments.AppendFormat("--lldb {0} ", Environment.GetEnvironmentVariable("LLDB_PATH") ?? throw new ArgumentException("LLDB_PATH environment variable not set"));

            // Add dotnet host program and arguments
            arguments.Append("--host \"");
            arguments.Append(config.HostExe);
            arguments.Append(" ");
            if (!string.IsNullOrWhiteSpace(config.HostArgs))
            {
                arguments.Append(config.HostArgs);
                arguments.Append(" ");
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
                WithEnvironmentVariable("DOTNET_ROOT", config.DotNetRoot()).
                WithLog(new TestRunner.TestLogger(outputHelper.IndentedOutput)).
                WithTimeout(TimeSpan.FromMinutes(10)).
                WithExpectedExitCode(0).
                WithWorkingDirectory(scriptDir).
                // Turn on stress logging so the dumplog and histinit commands pass
                WithEnvironmentVariable("COMPlus_LogFacility", "0xffffffbf").
                WithEnvironmentVariable("COMPlus_LogLevel", "6").
                WithEnvironmentVariable("COMPlus_StressLog", "1").
                WithEnvironmentVariable("COMPlus_StressLogSize", "65536");

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
