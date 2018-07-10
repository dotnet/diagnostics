// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostic.TestHelpers;
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

    public static IEnumerable<object[]> Configurations => TestRunConfiguration.Instance.Configurations.Select(c => new[] { c });

    private void SkipIfArm(TestConfiguration config)
    {
        if (config.TargetArchitecture == "arm" || config.TargetArchitecture == "arm64")
        {
            throw new SkipTestException("SOS does not support ARM architectures");
        }
    }

    private static bool IsCreateDumpConfig(TestConfiguration config)
    {
        return config.DebuggeeDumpOutputRootDir() != null;
    }

    private static bool IsOpenDumpConfig(TestConfiguration config)
    {
        return config.DebuggeeDumpInputRootDir() != null;
    }

    private async Task CreateDump(TestConfiguration config, string testName, string debuggeeName, string debuggeeArguments)
    {
        Directory.CreateDirectory(config.DebuggeeDumpOutputRootDir());

        using (SOSRunner runner = await SOSRunner.StartDebugger(config, Output, testName, debuggeeName, debuggeeArguments, loadDump: false, generateDump: true))
        {
            try
            {
                await runner.LoadSosExtension();

                string command = null;
                switch (runner.Debugger)
                {
                    case SOSRunner.NativeDebugger.Cdb:
                        await runner.ContinueExecution();
                        // On desktop create triage dump. On .NET Core, create full dump.
                        command = config.TestProduct.Equals("desktop") ? ".dump /o /mshuRp %DUMP_NAME%" : ".dump /o /ma %DUMP_NAME%";
                        break;
                    case SOSRunner.NativeDebugger.Gdb:
                        command = "generate-core-file %DUMP_NAME%";
                        break;
                    case SOSRunner.NativeDebugger.Lldb:
                        await runner.ContinueExecution();
                        command = OS.Kind == OSKind.OSX ? "process save-core %DUMP_NAME%" : "sos CreateDump %DUMP_NAME%";
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

        // Against a crash dump.
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

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task DivZero(TestConfiguration config)
    {
        await RunTest(config, "DivZero", "DivZero.script");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task GCTests(TestConfiguration config)
    {
        const string testName = "SOS.GCTests";
        const string debuggeeName = "GCWhere";

        // Live only
        SkipIfArm(config);
        using (SOSRunner runner = await SOSRunner.StartDebugger(config, Output, testName, debuggeeName, debuggeeArguments: null))
        {
            await runner.RunScript("GCTests.script");
        }
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task Overflow(TestConfiguration config)
    {
        await RunTest(config, "Overflow", "Overflow.script");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task Reflection(TestConfiguration config)
    {
        await RunTest(config, "ReflectionTest", "Reflection.script");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task SimpleThrow(TestConfiguration config)
    {
        await RunTest(config, "SimpleThrow", "SimpleThrow.script");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task NestedExceptionTest(TestConfiguration config)
    {
        await RunTest(config, "NestedExceptionTest", "NestedExceptionTest.script");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task TaskNestedException(TestConfiguration config)
    {
        await RunTest(config, "TaskNestedException", "TaskNestedException.script");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task StackTests(TestConfiguration config)
    {
        await RunTest(config, "SOS.StackTests", "NestedExceptionTest", null, "StackTests.script");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task StackAndOtherTests(TestConfiguration config)
    {
        SkipIfArm(config);
        if (config.BuildProjectMicrosoftNetCoreAppVersion.StartsWith("1.1"))
        {
            throw new SkipTestException("The debuggee (SymbolTestApp) doesn't work on .NET Core 1.1");
        }
        foreach (TestConfiguration currentConfig in TestRunner.EnumeratePdbTypeConfigs(config))
        {
            // This debuggee needs the directory of the exes/dlls to load the SymbolTestDll assembly.
            await RunTest(currentConfig, "SOS.StackAndOtherTests", "SymbolTestApp", "%DEBUG_ROOT%", "StackAndOtherTests.script");
        }
    }
}
