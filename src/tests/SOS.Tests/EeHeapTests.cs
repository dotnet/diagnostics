// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Structural coverage for the <c>!eeheap -gc</c> parser over real debuggee dumps: the regions/DATAS
/// layout (Core/SingleFile, and server GC = multi-heap) and the segment/ephemeral layout (.NET Framework).
/// Asserts the parsed model is well-formed and non-empty (so an empty/failed parse can never masquerade as
/// a pass) before the generation/region tests rely on <see cref="EeHeap.GenerationRanges"/>.
/// </summary>
public sealed class EeHeapTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task EeHeap_Structure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        EeHeap ee = target.EeHeap();
        AssertWellFormed(ee, expectMultiHeap: false, expectPoh: config.Flavor != Flavor.Framework);
    }

    // Server GC produces a multi-heap GC; capture the Scenarios target under the Server GC axis. Core-only
    // and dump-only (server-GC coverage flows through the Core dump path; see TestConfig.IsValid).
    public static TheoryData<TestConfig> ServerMatrix =>
        TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.Core, Host.AllValid, Liveness.Dump, GcType.Server);

    [SosTheory]
    [MemberData(nameof(ServerMatrix))]
    public async Task EeHeap_ServerGc_IsMultiHeap(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        EeHeap ee = target.EeHeap();
        Assert.True(ee.HeapCount > 1, $"server GC should report more than one heap, got {ee.HeapCount}");
        Assert.Equal(ee.HeapCount, ee.Heaps.Count);
        AssertWellFormed(ee, expectMultiHeap: true, expectPoh: true);

        // Each heap is independently well-formed with its own gen0/1/2 segments.
        Assert.All(ee.Heaps, h =>
        {
            Assert.NotEmpty(h.Gen0.Concat(h.Gen1).Concat(h.Gen2));
            Assert.NotNull(h.Address);
        });
    }

    private static void AssertWellFormed(EeHeap ee, bool expectMultiHeap, bool expectPoh)
    {
        Assert.NotEmpty(ee.Heaps);
        Assert.Equal(expectMultiHeap, ee.Heaps.Count > 1);

        // The footer sizes parsed and are positive.
        Assert.NotNull(ee.GcAllocatedHeapSize);
        Assert.NotNull(ee.GcCommittedHeapSize);
        Assert.True(ee.GcCommittedHeapSize!.Value.Decimal > 0);
        Assert.True(ee.GcCommittedHeapSize.Value.Decimal >= ee.GcAllocatedHeapSize!.Value.Decimal);

        // Every segment on every heap is well-formed.
        foreach (EeHeapNode h in ee.Heaps)
        {
            IEnumerable<EeHeapSegment> all = h.Gen0.Concat(h.Gen1).Concat(h.Gen2).Concat(h.Soh)
                .Concat(h.Loh).Concat(h.Poh).Concat(h.NonGc).Concat(h.Foh);
            foreach (EeHeapSegment s in all)
            {
                Assert.NotEqual(0ul, s.Begin);
                Assert.True(s.Begin <= s.Allocated, $"begin {s.Begin:x} <= allocated {s.Allocated:x}");
                Assert.True(s.Allocated <= s.Committed, $"allocated {s.Allocated:x} <= committed {s.Committed:x}");
                Assert.True(s.CommittedSize.Hex > 0);
                if (s.AllocatedSize is EeHeapSize a)
                {
                    Assert.Equal(s.Allocated - s.Begin, a.Hex);
                }
            }
        }

        // The generation primitive the T5 tests use is non-empty for the generations that always have
        // objects (gen2 holds runtime objects; LOH/POH hold the debuggee's big / pinned arrays).
        Assert.NotEmpty(ee.GenerationRanges(GcGeneration.Gen2));
        Assert.NotEmpty(ee.GenerationRanges(GcGeneration.Loh));
        if (expectPoh)
        {
            Assert.NotEmpty(ee.GenerationRanges(GcGeneration.Poh));
        }

        Assert.All(ee.GenerationRanges(GcGeneration.Gen2), r => Assert.True(r.Start <= r.End));
    }
}
