// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.TestHelpers;
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

    private async Task RunTest(string scriptName, bool testLive = true, bool testDump = true, SOSRunner.TestInformation information = null)
    {
        information.OutputHelper = Output;

        // TODO: enable when the Alpine images (we are currently using 3.13) have the py3-lldb package installed.
        // TODO: enable either when bpmd is fixed on Alpine or the bpmd tests are ifdef'ed out of the scripts for Alpine
        if (testLive && !SOSRunner.IsAlpine())
        {
            // Live
            using (SOSRunner runner = await SOSRunner.StartDebugger(information, SOSRunner.DebuggerAction.Live))
            {
                await runner.RunScript(scriptName);
            }
        }

        // TODO: enable for 6.0 when PR https://github.com/dotnet/runtime/pull/56272 is merged/released
        if (testDump && !SOSRunner.IsAlpine())
        {
            // Create and test dumps on OSX only if the runtime is 6.0 or greater
            // TODO: reenable for 5.0 when the MacOS createdump fixes make it into a service release (https://github.com/dotnet/diagnostics/issues/1749)
            if (OS.Kind != OSKind.OSX || information.TestConfiguration.RuntimeFrameworkVersionMajor > 5)
            {
                // Generate a crash dump.
                if (information.TestConfiguration.DebuggeeDumpOutputRootDir() != null)
                {
                    await SOSRunner.CreateDump(information);
                }

                // Test against a crash dump.
                if (information.TestConfiguration.DebuggeeDumpInputRootDir() != null)
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
            }
        }
    }

    private async Task RunTest(TestConfiguration config, string debuggeeName, string scriptName, string testName = null, bool testLive = true, bool testDump = true, bool testTriage = false)
    {
        await RunTest(scriptName, testLive, testDump, new SOSRunner.TestInformation {
            TestConfiguration = config,
            TestName = testName,
            DebuggeeName = debuggeeName,
            DumpType = SOSRunner.DumpType.Heap
        });

        if (testTriage)
        {
            await RunTest(scriptName, testLive: false, testDump, new SOSRunner.TestInformation {
                TestConfiguration = config,
                TestName = testName,
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
        if (!config.IsNETCore || config.RuntimeFrameworkVersionMajor < 5)
        {
            throw new SkipTestException("This test validates POH behavior, which was introduced in .net 5");
        }
        await RunTest(config, "GCPOH", "GCPOH.script", testName: "SOS.GCPOHTests", testDump: false);
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task Overflow(TestConfiguration config)
    {
        // The .NET Core createdump facility may not catch stack overflow so use gdb to generate dump
        await RunTest("Overflow.script", information: new SOSRunner.TestInformation {
            TestConfiguration = config,
            DebuggeeName = "Overflow",
            // Generating the logging for overflow test causes so much output from createdump that it hangs/timesout the test run
            DumpDiagnostics = false,
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
        await RunTest("OtherCommands.script", information: new SOSRunner.TestInformation {
            TestConfiguration = config,
            TestName = "SOS.OtherCommands",
            DebuggeeName = "SymbolTestApp",
            DebuggeeArguments = "%DEBUG_ROOT%",
        });
    }

    [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "SOS.StackAndOtherTests")]
    public async Task StackAndOtherTests(TestConfiguration config)
    {
        foreach (TestConfiguration currentConfig in TestRunner.EnumeratePdbTypeConfigs(config))
        {
            // This debuggee needs the directory of the exes/dlls to load the SymbolTestDll assembly.
            await RunTest("StackAndOtherTests.script", information: new SOSRunner.TestInformation {
                TestConfiguration = currentConfig,
                TestName = "SOS.StackAndOtherTests",
                DebuggeeName = "SymbolTestApp",
                DebuggeeArguments = "%DEBUG_ROOT%",
                DumpNameSuffix = currentConfig.DebugType
            });

            // This tests using regular Windows PDBs with no managed hosting. SOS should fallback 
            // to using native implementations of the host/target/runtime.
            if (currentConfig.AllSettings["DebugType"] == "full")
            {
                var settings = new Dictionary<string, string>(currentConfig.AllSettings) {
                    ["SetHostRuntime"] = "-none"
                };
                await RunTest("StackAndOtherTests.script", information: new SOSRunner.TestInformation {
                    TestConfiguration = new TestConfiguration(settings),
                    TestName = "SOS.StackAndOtherTests",
                    DebuggeeName = "SymbolTestApp",
                    DebuggeeArguments = "%DEBUG_ROOT%",
                    DumpNameSuffix = currentConfig.DebugType
                });
            }
        }
    }

    [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "SOS.WebApp")]
    public async Task WebApp(TestConfiguration config)
    {
        await RunTest("WebApp.script", testLive: false, information: new SOSRunner.TestInformation {
            TestConfiguration = config,
            DebuggeeName = "WebApp",
            UsePipeSync = true,
            DumpGenerator = SOSRunner.DumpGenerator.DotNetDump
        });
    }

    [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "SOS.WebApp3")]
    public async Task WebApp3(TestConfiguration config)
    {
        await RunTest("WebApp.script", testLive: false, information: new SOSRunner.TestInformation {
            TestConfiguration = config,
            DebuggeeName = "WebApp3",
            UsePipeSync = true,
            DumpGenerator = SOSRunner.DumpGenerator.DotNetDump
        });
    }

    [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "SOS.DualRuntimes")]
    public async Task DualRuntimes(TestConfiguration config)
    {
        // The assembly path, class and function name of the desktop test code to load/run
        string desktopTestParameters = TestConfiguration.MakeCanonicalPath(config.GetValue("DesktopTestParameters"));
        if (string.IsNullOrEmpty(desktopTestParameters))
        {
            throw new SkipTestException("DesktopTestParameters config value does not exists");
        }
        await RunTest("DualRuntimes.script", testLive: false, information: new SOSRunner.TestInformation {
            TestConfiguration = config,
            TestName = "SOS.DualRuntimes",
            DebuggeeName = "WebApp3",
            DebuggeeArguments = desktopTestParameters,
            UsePipeSync = true,
            DumpGenerator = SOSRunner.DumpGenerator.DotNetDump
        });
    }

    [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "DotnetDumpCommands")]
    public async Task ConcurrentDictionaries(TestConfiguration config)
    {
        await RunTest("ConcurrentDictionaries.script", testLive: false, information: new SOSRunner.TestInformation
        {
            TestConfiguration = config,
            DebuggeeName = "DotnetDumpCommands",
            DebuggeeArguments = "dcd",
            DumpNameSuffix = "dcd",
            UsePipeSync = true,
            DumpGenerator = SOSRunner.DumpGenerator.DotNetDump,
        });
    }

    [SkippableTheory, MemberData(nameof(GetConfigurations), "TestName", "DotnetDumpCommands")]
    public async Task DumpGen(TestConfiguration config)
    {
        await RunTest("DumpGen.script", testLive: false, information: new SOSRunner.TestInformation
        {
            TestConfiguration = config,
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
        if (OS.Kind == OSKind.Windows || config.IsDesktop || config.RuntimeFrameworkVersionMajor == 1 || SOSRunner.IsAlpine())
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
