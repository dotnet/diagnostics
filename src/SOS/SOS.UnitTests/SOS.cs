using Debugger.Tests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public static IEnumerable<object[]> Configurations
    {
        get
        {
            return TestRunConfiguration.Instance.Configurations.Select(c => new[] { c });
        }
    }

    private void SkipIfArm(TestConfiguration config)
    {
        if (config.BuildProjectRuntime == "linux-arm" || config.BuildProjectRuntime == "linux-arm64" || config.BuildProjectRuntime == "win-arm" || config.BuildProjectRuntime == "win7-arm64")
        {
            throw new SkipTestException("SOS does not support ARM architectures");
        }
    }

    private static bool IsCreateDumpConfig(TestConfiguration config)
    {
        return config.DebuggeeDumpOutputRootDir != null;
    }

    private static bool IsOpenDumpConfig(TestConfiguration config)
    {
        return config.DebuggeeDumpInputRootDir != null;
    }

    private async Task CreateDump(TestConfiguration config, string testName, string debuggeeName, string debuggeeArguments)
    {
        Directory.CreateDirectory(config.DebuggeeDumpOutputRootDir);

        using (SOSRunner runner = await SOSRunner.StartDebugger(config, Output, testName, debuggeeName, debuggeeArguments, loadDump: false, generateDump: true))
        {
            try
            {
                await runner.LoadSosExtension();
                await runner.ContinueExecution();

                string command = null;
                switch (runner.Debugger)
                {
                    case SOSRunner.NativeDebugger.Cdb:
                        if (config.TestProduct.Equals("desktop"))
                        {
                            // On desktop create triage dump
                            command = ".dump /o /mshuRp %DUMP_NAME%";
                        }
                        else
                        {
                            // On .NET Core, create full dump
                            command = ".dump /o /ma %DUMP_NAME%";
                        }
                        break;
                    case SOSRunner.NativeDebugger.Gdb:
                        command = "generate-core-file %DUMP_NAME%";
                        break;
                    case SOSRunner.NativeDebugger.Lldb:
                        command = "sos CreateDump %DUMP_NAME%";
                        break;
                    default:
                        throw new Exception(runner.Debugger.ToString() + " does not support creating dumps");
                }

                await runner.RunCommand(command);
                await runner.QuitDebugger();
            }
            catch (Exception ex)
            {
                runner.WriteLine(ex.ToString());
                throw;
            }
        }
    }

    private Task RunTest(TestConfiguration config, string debuggeeName, string scriptName)
    {
        return RunTest(config, "SOS." + debuggeeName, debuggeeName, null, scriptName);
    }

    private async Task RunTest(TestConfiguration config, string testName, string debuggeeName, string debuggeeArguments, string scriptName)
    {
        SkipIfArm(config);

        // Live
        using (SOSRunner runner = await SOSRunner.StartDebugger(config, Output, testName, debuggeeName, debuggeeArguments))
        {
            await runner.RunScript(scriptName);
        }

        // Against a crash dump
        if (IsCreateDumpConfig(config))
        {
            await CreateDump(config, testName, debuggeeName, debuggeeArguments);
        }

        if (IsOpenDumpConfig(config))
        {
            using (SOSRunner runner = await SOSRunner.StartDebugger(config, Output, testName, debuggeeName, debuggeeArguments, loadDump: true))
            {
                await runner.RunScript(scriptName);
            }
        }
    }

#if OSX_FAIL_WITH_BUG
    [SkippableTheory(Skip = "SOS tests not working for OS X"), MemberData("Configurations")]
#else
    [SkippableTheory, MemberData("Configurations")]
#endif
    public async Task DivZero(TestConfiguration config)
    {
        await RunTest(config, "DivZero", "SoS/DivZero.script");
    }

#if OSX_FAIL_WITH_BUG
    [SkippableTheory(Skip = "SOS tests not working for OS X"), MemberData("Configurations")]
#else
    [SkippableTheory, MemberData("Configurations")]
#endif
    public async Task GCTests(TestConfiguration config)
    {
        const string testName = "SOS.GCTests";
        const string debuggeeName = "GCWhere";

        // Live only
        SkipIfArm(config);
        using (SOSRunner runner = await SOSRunner.StartDebugger(config, Output, testName, debuggeeName, debuggeeArguments: null))
        {
            await runner.RunScript("SoS/GCTests.script");
        }
    }

#if OSX_FAIL_WITH_BUG
    [SkippableTheory(Skip = "SOS tests not working for OS X"), MemberData("Configurations")]
#else
    [SkippableTheory, MemberData("Configurations")]
#endif
    public async Task Overflow(TestConfiguration config)
    {
        await RunTest(config, "Overflow", "SoS/Overflow.script");
    }

#if OSX_FAIL_WITH_BUG
    [SkippableTheory(Skip = "SOS tests not working for OS X"), MemberData("Configurations")]
#else
    [SkippableTheory, MemberData("Configurations")]
#endif
    public async Task Reflection(TestConfiguration config)
    {
        await RunTest(config, "ReflectionTest", "SoS/Reflection.script");
    }

#if OSX_FAIL_WITH_BUG
    [SkippableTheory(Skip = "SOS tests not working for OS X"), MemberData("Configurations")]
#else
    [SkippableTheory, MemberData("Configurations")]
#endif
    public async Task SimpleThrow(TestConfiguration config)
    {
        await RunTest(config, "SimpleThrow", "SoS/SimpleThrow.script");
    }

#if OSX_FAIL_WITH_BUG
    [SkippableTheory(Skip = "SOS tests not working for OS X"), MemberData("Configurations")]
#else
    [SkippableTheory, MemberData("Configurations")]
#endif
    public async Task NestedExceptionTest(TestConfiguration config)
    {
        await RunTest(config, "NestedExceptionTest", "SoS/NestedExceptionTest.script");
    }

#if OSX_FAIL_WITH_BUG
    [SkippableTheory(Skip = "SOS tests not working for OS X"), MemberData("Configurations")]
#else
    [SkippableTheory, MemberData("Configurations")]
#endif
    public async Task TaskNestedException(TestConfiguration config)
    {
        await RunTest(config, "TaskNestedException", "SoS/TaskNestedException.script");
    }

    [SkippableTheory(Skip = "Test issue: Test build system can't yet create the debuggee"), MemberData("Configurations")]
    public async Task WinRTAsync(TestConfiguration config)
    {
        await RunTest(config, "RSSFeed", "SoS/WinRTAsync.script");
    }

#if OSX_FAIL_WITH_BUG
    [SkippableTheory(Skip = "SOS tests not working for OS X"), MemberData("Configurations")]
#else
    [SkippableTheory, MemberData("Configurations")]
#endif
    public async Task StackTests(TestConfiguration config)
    {
        if (config.BuildProjectRuntime == "linux-x64")
        {
            throw new SkipTestException("SOS StackTests are disabled on linux. Bug #584221");
        }
        await RunTest(config, "SOS.StackTests", "NestedExceptionTest", null, "SoS/StackTests.script");
    }

#if OSX_FAIL_WITH_BUG
    [SkippableTheory(Skip = "SOS tests not working for OS X"), MemberData("Configurations")]
#else
    [SkippableTheory, MemberData("Configurations")]
#endif
    public async Task StackAndOtherTests(TestConfiguration config)
    {
        SkipIfArm(config);
        if (config.BuildProjectRuntime == "linux-x64")
        {
            throw new SkipTestException("SOS StackTests are disabled on linux. Bug #584221");
        }

        foreach (TestConfiguration currentConfig in TestRunner.EnumeratePdbTypeConfigs(config))
        {
            // This debuggee needs the directory of the exes/dlls to load the symboltestdll assembly.
            await RunTest(currentConfig, "SOS.StackAndOtherTests", "symboltestapp", "%DEBUG_ROOT%", "SoS/StackAndOtherTests.script");
        }
    }
}
