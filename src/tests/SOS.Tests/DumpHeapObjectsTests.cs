// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for <c>!dumpheap</c>'s object listing + statistics output and the size filters, from one
/// loaded dump of the <see cref="TargetCatalog.Scenarios"/> target (which holds a
/// uniquely-named live object and a known-large rooted array). Flags exercised:
/// default, <c>-stat</c>, <c>-bycount</c>, <c>-type</c>, <c>-mt</c>, <c>-short</c>, <c>-min</c>, <c>-max</c>.
/// Correctness comes from cross-flag self-consistency (the <c>-stat</c> table equals the default's
/// statistics; <c>-short</c> of a <c>-type</c> filter equals that type's object addresses; <c>-type</c>
/// and <c>-mt</c> select the same object) and an exact size oracle (the array's declared
/// <see cref="TestTargets.SosHarnessScenarios.BigArraySize"/>, mirrored from the debuggee).
/// </summary>
public sealed class DumpHeapObjectsTests
{
    // Live opt-in: !dumpheap is the representative live GC heap walk (it enumerates the live heap's
    // segments/regions), so the base statistics test runs dump AND live; the dumpheap variations elsewhere
    // (-strings, -thinlock, -live/-dead, generations) stay dump-only.
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], liveness: Liveness.AllValid);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpHeap_ObjectsStatisticsAndSize(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        // --- default: object listing + statistics + the "Total N objects, M bytes" footer ---
        DumpHeapResult full = target.DumpHeap();
        Assert.NotEmpty(full.Objects);
        Assert.NotEmpty(full.Statistics);
        Assert.Equal(full.Statistics.Sum(r => r["Count"].AsInt64(Sos.Integer)), full.TotalObjects);
        Assert.Equal(full.Statistics.Sum(r => r["TotalSize"].AsInt64(Sos.Integer)), full.TotalBytes);

        // --- -stat: same statistics, but no per-object listing ---
        DumpHeapResult stat = target.DumpHeap("-stat");
        Assert.Equal(full.TotalObjects, stat.TotalObjects);
        Assert.Equal(full.Statistics.Length, stat.Statistics.Length);
        Assert.Throws<SosAssertException>(() => stat.Objects); // -stat omits the object listing

        // --- default sorts statistics by TotalSize, -bycount by Count (both ascending) ---
        AssertNonDecreasing(full.Statistics.Select(r => r["TotalSize"].AsInt64(Sos.Integer)), "default by TotalSize");

        // Both hosts load the freshly-built repo SOS, which has -bycount.
        AssertNonDecreasing(target.DumpHeap("-bycount").Statistics.Select(r => r["Count"].AsInt64(Sos.Integer)), "-bycount by Count");

        // --- -type / -mt select the same single object; -short of that filter is its address ---
        const string uniqueType = "LiveUniqueMarker";
        DumpHeapResult byType = target.DumpHeap($"-type {uniqueType}");
        SosRow stats = Assert.Single(byType.Statistics, r => r["Class Name"].Value == uniqueType);
        Assert.Equal(1, stats["Count"].AsInt32(Sos.Integer));
        ulong mt = stats["MT"].AsUInt64(Sos.Addr);
        SosRow obj = Assert.Single(byType.Objects, r => r["MT"].AsUInt64(Sos.Addr) == mt);
        ulong address = obj["Address"].AsUInt64(Sos.Addr);

        ulong byMt = Assert.Single(target.DumpHeap($"-mt {mt:x}").Objects)["Address"].AsUInt64(Sos.Addr);
        Assert.Equal(address, byMt);

        ulong shortAddr = Assert.Single(target.DumpHeap($"-type {uniqueType} -short").ShortAddresses);
        Assert.Equal(address, shortAddr);

        Assert.Equal(address, target.FindUniqueObject(uniqueType));

        // --- -min / -max bracket the known-large array by its declared size, in ONE cheap query (a
        // tiny result set). NOTE: SOS parses -min/-max as DECIMAL on every host/flavor, even though the
        // command help says "(hex)". ---
        int big = TestTargets.SosHarnessScenarios.BigArraySize;
        DumpHeapResult bracket = target.DumpHeap($"-type System.Byte[] -min {big} -max {big + 4096}");
        Assert.NotEmpty(bracket.Objects); // the large array is in [big, big+4096]
        bracket.Objects.AssertAll(
            r => { long s = r["Size"].AsInt64(Sos.Integer); return s >= big && s <= big + 4096; },
            $"every bracketed row Size in [{big}, {big + 4096}]");

        // The small unique object is the opposite: it passes -max and fails -min.
        Assert.Equal(1, target.DumpHeap($"-type {uniqueType} -max {big - 1}").CountOf(uniqueType));
        Assert.Equal(0, target.DumpHeap($"-type {uniqueType} -min {big}").CountOf(uniqueType));
    }

    private static void AssertNonDecreasing(IEnumerable<long> values, string description)
    {
        long previous = long.MinValue;
        foreach (long value in values)
        {
            Assert.True(value >= previous, $"{description}: expected non-decreasing, but {value} < {previous}.");
            previous = value;
        }
    }
}
