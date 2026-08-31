// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// GC / heap inspection commands the legacy <c>.script</c> suite exercised but the modern harness did not:
/// <c>!gcroot</c>, <c>!objsize</c>, <c>!gchandles</c>, <c>!finalizequeue</c>, <c>!gcheapstat</c>,
/// <c>!verifyheap</c>, and <c>!dumpgen</c>. Each is anchored on the debuggee's known objects (the rooted
/// live marker, the dropped dead marker, and the field-rich <c>FieldMarker</c>) so the assertions are real
/// oracles, not just "the command ran".
/// </summary>
public sealed class GcInspectionTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task GcRoot_FindsRootsForLive_NoneForDead(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // The live marker is rooted by a static, so gcroot finds at least one root and names the object.
        ulong live = target.FindUniqueObject("LiveUniqueMarker");
        SosOutput liveRoots = target.Sos($"gcroot {live:x}");
        liveRoots.AssertContains("LiveUniqueMarker");
        Assert.True(UniqueRootCount(liveRoots) > 0, $"expected roots for the live marker:\n{liveRoots.Text}");

        // The dead marker is unreachable (only uncollected), so gcroot finds no roots.
        ulong dead = target.FindUniqueObject("DeadUniqueMarker");
        SosOutput deadRoots = target.Sos($"gcroot {dead:x}");
        Assert.Equal(0, UniqueRootCount(deadRoots));
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task ObjSize_CountsTransitiveClosure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // The fieldless live marker keeps only itself alive.
        ulong live = target.FindUniqueObject("LiveUniqueMarker");
        (int liveCount, _) = ObjSizeTotal(target.Sos($"objsize {live:x}"));
        Assert.Equal(1, liveCount);

        // FieldMarker keeps its string + int[] + signature byte[]s alive, so its closure is strictly bigger.
        ulong fields = target.FindUniqueObject("FieldMarker");
        (int fieldCount, long fieldBytes) = ObjSizeTotal(target.Sos($"objsize {fields:x}"));
        Assert.True(fieldCount > liveCount, $"FieldMarker closure ({fieldCount}) should exceed the marker's ({liveCount})");
        Assert.True(fieldBytes > 0);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task GcHandles_ReportsHandleSummary(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput handles = target.Sos("gchandles -stat");
        handles.AssertContains("Handles:");
        Match strong = Regex.Match(handles.Text, @"Strong Handles:\s+(\d+)");
        Assert.True(strong.Success && int.Parse(strong.Groups[1].Value) > 0, $"expected strong handles:\n{handles.Text}");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task FinalizeQueue_ReportsStructure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput fq = target.Sos("finalizequeue -stat");
        fq.AssertContains("finalizable objects");
        Assert.Matches(@"generation 0 has \d+ objects", fq.Text);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task GcHeapStat_ReportsGenerationSizes(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput stat = target.Sos("gcheapstat");
        // The allocated-size table has a per-heap row; gen0 always holds the bulk of our fresh allocations.
        Match heap0 = Regex.Match(stat.Text, @"Heap0\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.Multiline);
        Assert.True(heap0.Success, $"expected a Heap0 row:\n{stat.Text}");
        Assert.True(long.Parse(heap0.Groups[1].Value) > 0, "gen0 allocated size should be positive");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task VerifyHeap_ReportsNoCorruption(TestConfig config)
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
        target.GoToStopPoint(TargetCatalog.StopGen1);

        SosOutput verify = target.Sos("verifyheap");
        Assert.Matches(@"\b0 errors\b", verify.Text);
        verify.AssertContains("No heap corruption detected");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpGen_ListsGenerationObjects(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // dumpgen gen0 is the dedicated "objects in a generation" command; our thin-lock marker lives there.
        SosOutput gen0 = target.Sos("dumpgen gen0");
        gen0.AssertContains("ThinLockMarker");
    }

    private static int UniqueRootCount(SosOutput output)
    {
        Match m = Regex.Match(output.Text, @"Found (\d+) unique roots");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    private static (int Count, long Bytes) ObjSizeTotal(SosOutput output)
    {
        Match m = Regex.Match(output.Text, @"Total ([\d,]+) objects,?\s*([\d,]+) bytes");
        Assert.True(m.Success, $"expected an objsize total:\n{output.Text}");
        return (int.Parse(m.Groups[1].Value.Replace(",", "")), long.Parse(m.Groups[2].Value.Replace(",", "")));
    }
}
