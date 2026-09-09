// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Runtime / module / process listing commands the legacy <c>.script</c> suite exercised: <c>!eeversion</c>,
/// <c>!modules</c>, <c>!clrmodules</c>, <c>!assemblies</c>, <c>!runtimes</c>, <c>!registers</c>,
/// <c>!threads</c>, and <c>!dumpruntimetypes</c>. These are listing/identity commands, so the assertions
/// check that the expected entities (the debuggee's own module, the runtime, the worker threads, a known
/// type) appear.
/// </summary>
public sealed class RuntimeInfoTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task EeVersion_ReportsRuntimeAndSosVersion(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput ee = target.Sos("eeversion");
        Assert.Matches(@"\d+\.\d+\.\d+", ee.Text); // the runtime version
        ee.AssertContains("SOS Version:");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task ClrModulesAndAssemblies_ListDebuggeeModule(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // The CLR module list and the assembly list both include the debuggee, on every host.
        target.Sos("clrmodules").AssertContains("SosHarnessScenarios");
        target.Sos("assemblies").AssertContains("SosHarnessScenarios");
    }

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task Modules_Registers_Threads_DotnetDumpOnly(TestConfig config)
    {
        // modules, registers and threads are provided by the dotnet-dump REPL; the dbgeng (cdb) host uses
        // the native debugger's lm / r / ~ instead, so these names exist only under dotnet-dump.
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("modules").AssertContains("SosHarnessScenarios");
        string stackPointerRegister = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "esp",
            Architecture.X64 => "rsp",
            Architecture.Arm or Architecture.Arm64 => "sp",
            _ => throw new PlatformNotSupportedException(),
        };
        target.Sos("registers").AssertContains(stackPointerRegister);

        // The debuggee parks several worker threads, so the thread list has multiple entries.
        SosOutput threads = target.Sos("threads");
        Assert.True(Regex.Matches(threads.Text, @"0x[0-9a-fA-F]+\s+\(\d+\)").Count >= 2,
            $"expected multiple threads:\n{threads.Text}");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Runtimes_ReportsLoadedRuntime(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput runtimes = target.Sos("runtimes");
        Assert.Contains(".NET", runtimes.Text, StringComparison.Ordinal); // ".NET Core runtime" or ".NET Framework"
        Assert.Matches(@"\d+\.\d+\.\d+", runtimes.Text);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpRuntimeTypes_ListsRuntimeTypeObjects(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput types = target.Sos("dumpruntimetypes");
        types.AssertContains("Type Name");
        types.AssertContains("System."); // at least the framework RuntimeType objects
    }
}
