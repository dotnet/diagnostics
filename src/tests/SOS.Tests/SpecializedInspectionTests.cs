// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Commands that need specific runtime state, which the debuggee now stages at the heap stop:
/// <c>!timerinfo</c> (a registered timer), <c>!threadpool</c> (a parked work item initialises the pool),
/// <c>!syncblk</c> (a contended monitor inflated to a sync block), <c>!dumpasync</c> (a suspended async
/// state machine), and <c>!dcd</c> (a populated <c>ConcurrentDictionary</c>). The legacy <c>.script</c>
/// suite exercised these via dedicated debuggees; here one consolidated debuggee supplies the state.
/// </summary>
public sealed class SpecializedInspectionTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);
    public static TheoryData<TestConfig> CoreRuntimeMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.Core | Flavor.SingleFile);

    // !threadpool reads native ThreadPool state that a net8 reduced Heap dump doesn't carry (fixed from net9 on);
    // capture a Full dump for net8 only. net9+ works on the default Heap dump.
    public static TheoryData<TestConfig> ThreadPoolMatrix => TestMatrices.FullDumpOnCoreVersions([TargetCatalog.Scenarios], CoreVersion.Net8);

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task TimerInfo_ReportsRegisteredTimer(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // timerinfo is a managed extension command (dotnet-dump only). The debuggee keeps a long-due-time
        // timer registered.
        Assert.Matches(@"[1-9]\d* timers", target.Sos("timerinfo").Text);
    }

    [SosTheory]
    [MemberData(nameof(ThreadPoolMatrix))]
    public async Task ThreadPool_ReportsWorkerStats(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // A parked work item has initialised the pool, so its worker stats are reportable.
        SosOutput pool = target.Sos("threadpool");
        pool.AssertContains("CPU utilization");
        pool.AssertContains("Workers Total");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task SyncBlk_ReportsInflatedMonitor(TestConfig config)
    {
        if (OperatingSystem.IsMacOS() &&
            config.Host == Host.DotnetDump &&
            config.Dac == Dac.Legacy &&
            config.Flavor == Flavor.Core &&
            config.CoreVersion == CoreVersion.Net11)
        {
            HarnessSkipException.Now(
                "https://github.com/dotnet/diagnostics/issues/5985: legacy DAC SyncBlock data is unavailable for macOS .NET 11 dumps.");
        }

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // The contended s_fatLock is inflated, so syncblk lists a held sync block (MonitorHeld > 0).
        SosOutput sync = target.Sos("syncblk");
        sync.AssertContains("MonitorHeld");
        Assert.True(
            Regex.IsMatch(sync.Text, @"^\s*\d+\s+[0-9a-fA-F`]+\s+[1-9]\d*\s+\d+\s+", RegexOptions.Multiline),
            $"expected a held sync block:\n{sync.Text}");
    }

    [SosTheory]
    [MemberData(nameof(CoreRuntimeMatrix))]
    public async Task DumpAsync_ShowsSuspendedStateMachine(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // The SuspendedAsync state machine is parked at its await, so dumpasync finds it. (dumpasync walks
        // the modern .NET async-task representation; desktop .NET Framework predates it, so Core-only.)
        target.Sos("dumpasync").AssertContains("SuspendedAsync");
    }

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task Dcd_DumpsConcurrentDictionary(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // dcd is a managed extension command (dotnet-dump only).
        ulong dict = FindConcurrentDictionary(target);
        SosOutput dcd = target.Sos($"dcd {dict:x}");
        dcd.AssertContains("ConcurrentDictionary<System.Int32, System.String>");
        Assert.Matches(@"Key:\s+1", dcd.Text);
        dcd.AssertContains("\"one\"");
    }

    // The debuggee's ConcurrentDictionary<int, string>. A -type filter also matches its nested
    // +Tables/+Node/+VolatileNode[] types, so select the exact-named method table and resolve its instance.
    private static ulong FindConcurrentDictionary(Target target)
    {
        const string typeName = "System.Collections.Concurrent.ConcurrentDictionary<System.Int32, System.String>";
        // dumpheap -type takes a single token, so filter on the space-free type prefix, then pick the row
        // whose full class name is exactly the dictionary (not its nested +Tables/+Node/+VolatileNode[]).
        SosRow row = target.DumpHeap("-type System.Collections.Concurrent.ConcurrentDictionary").Statistics
            .SingleRow(r => r["Class Name"].Value == typeName, $"a single {typeName} method table");
        ulong mt = row["MT"].AsUInt64(Sos.Addr);
        return Assert.Single(target.DumpHeap($"-mt {mt:x} -short").ShortAddresses);
    }
}
