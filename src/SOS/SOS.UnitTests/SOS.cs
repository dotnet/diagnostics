// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostic.TestHelpers;
using System.Collections.Generic;
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

    private Task RunTest(TestConfiguration config, string debuggeeName, string scriptName, bool useCreateDump = true)
    {
        return RunTest(config, "SOS." + debuggeeName, debuggeeName, scriptName, useCreateDump: useCreateDump);
    }

    private async Task RunTest(TestConfiguration config, string testName, string debuggeeName, string scriptName, string debuggeeArguments = null, bool useCreateDump = true)
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
            await SOSRunner.CreateDump(config, Output, testName, debuggeeName, debuggeeArguments, useCreateDump);
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
        // Live only
        SkipIfArm(config);
        using (SOSRunner runner = await SOSRunner.StartDebugger(config, Output, testName: "SOS.GCTests", debuggeeName: "GCWhere"))
        {
            await runner.RunScript("GCTests.script");
        }
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task Overflow(TestConfiguration config)
    {
        // The .NET Core createdump facility may not catch stack overflow so use gdb to generate dump
        await RunTest(config, "Overflow", "Overflow.script", useCreateDump: config.StackOverflowCreatesDump);
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
        await RunTest(config, "SOS.StackTests", "NestedExceptionTest", "StackTests.script");
    }

    [SkippableTheory, MemberData(nameof(Configurations))]
    public async Task StackAndOtherTests(TestConfiguration config)
    {
        SkipIfArm(config);
        if (config.RuntimeFrameworkVersionMajor == 1)
        {
            throw new SkipTestException("The debuggee (SymbolTestApp) doesn't work on .NET Core 1.1 because of a AssemblyLoadContext problem");
        }
        foreach (TestConfiguration currentConfig in TestRunner.EnumeratePdbTypeConfigs(config))
        {
            // This debuggee needs the directory of the exes/dlls to load the SymbolTestDll assembly.
            await RunTest(currentConfig, "SOS.StackAndOtherTests", "SymbolTestApp", "StackAndOtherTests.script", debuggeeArguments: "%DEBUG_ROOT%");
        }
    }
}
