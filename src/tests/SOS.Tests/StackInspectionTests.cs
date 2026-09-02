// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Stack inspection commands from the legacy <c>.script</c> suite: <c>!dumpstackobjects</c>/<c>!dso</c> and
/// <c>!parallelstacks</c> (both hosts), and <c>!dumpstack</c>/<c>!eestack</c> (the native+managed stack
/// walk, which needs the debugger's stack walker and so is dbgeng/cdb-only — dotnet-dump reports them
/// unrecognized). Anchored at the args/locals stop, where <c>ArgsLocalsMethod</c> has uniquely-typed
/// argument and local objects on the stack.
/// </summary>
public sealed class StackInspectionTests
{
    // Live opt-in: !dso (dumpstackobjects) scans a live thread's stack memory for object references, so
    // DumpStackObjects_ListsStackRoots runs dump AND live; the other stack commands here stay dump-only.
    public static TheoryData<TestConfig> Matrix =>
        TestMatrices.CurrentThreadCommands([TargetCatalog.Scenarios], Liveness.AllValid);
    public static TheoryData<TestConfig> CdbMatrix => TestMatrices.FullDumpOnCoreVersions([TargetCatalog.Scenarios], CoreVersion.Net8 | CoreVersion.Net9 | CoreVersion.Net10, Flavor.AllValid, Host.Cdb);
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpStackObjects_ListsStackRoots(TestConfig config)
    {
        TestMatrices.SkipUnavailableMacOsDotnetDumpThreads(config);

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopArgsLocals);

        // The argument and local marker objects of ArgsLocalsMethod are live on the current thread's stack.
        ulong arg = target.FindUniqueObject("ArgUniqueMarker");
        ulong local = target.FindUniqueObject("LocalUniqueMarker");

        SosOutput dso = target.Sos("dumpstackobjects");
        dso.AssertContains("ArgUniqueMarker");
        dso.AssertContains("LocalUniqueMarker");
        Assert.Contains(arg.ToString("x"), dso.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(local.ToString("x"), dso.Text, StringComparison.OrdinalIgnoreCase);

        // dso is the documented alias and prints the same listing.
        target.Sos($"dso").AssertContains("LocalUniqueMarker");
    }

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task ParallelStacks_GroupsThreadsByCallStack(TestConfig config)
    {
        TestMatrices.SkipUnavailableMacOsDotnetDumpThreads(config);

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopArgsLocals);

        // parallelstacks is a managed extension command that only the dotnet-dump host exports.
        SosOutput ps = target.Sos("parallelstacks");
        ps.AssertContains("SosHarnessScenarios.ArgsLocalsMethod");
        Assert.Matches(@"\d+ threads", ps.Text); // the "==> N threads with M roots" footer
    }

    [WindowsTheory]
    [MemberData(nameof(CdbMatrix))]
    public async Task DumpStack_WalksNativeAndManagedFrames(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopArgsLocals);

        SosOutput stack = target.Sos("dumpstack");
        stack.AssertContains("SosHarnessScenarios.ArgsLocalsMethod");
        stack.AssertContains("SosHarnessScenarios.Main()");
    }

    [WindowsTheory]
    [MemberData(nameof(CdbMatrix))]
    public async Task EeStack_WalksAllThreads(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopArgsLocals);

        // eestack is dumpstack across every managed thread, so the args/locals frame still appears.
        target.Sos("eestack").AssertContains("SosHarnessScenarios.ArgsLocalsMethod");
    }
}
