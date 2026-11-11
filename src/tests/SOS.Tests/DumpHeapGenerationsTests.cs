// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// T5: <c>!dumpheap</c> generation / region filtering (<c>-gen</c>, <c>-segment</c>, <c>-heap</c>, and the
/// positional <c>[start end]</c> range), validated against the GC heap layout from the <see cref="EeHeap"/>
/// parser. The oracle is geometric: the address of every object <c>dumpheap</c> reports for a generation
/// must fall inside one of that generation's segment ranges as reported by <c>eeheap -gc</c>. Every
/// assertion is guarded against a silent empty pass — both the eeheap ranges AND the dumpheap object set are
/// required to be non-empty before membership is checked. The legacy <c>-startAtLowerBound</c> switch lives
/// only in the help text (no <c>[Option]</c> in the modern command), so it is asserted to be rejected.
/// </summary>
public sealed class DumpHeapGenerationsTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpHeap_GenerationsAndRegions(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        EeHeap ee = target.EeHeap();

        // --- -gen <g>: every enumerated object is inside that generation's eeheap ranges. ---
        // At the heap stop nothing has been promoted yet, so gen0 holds essentially everything, loh holds
        // the debuggee's big array and poh the pinned array (Core only). gen1/gen2 are typically empty
        // here, so they're checked for membership only (their eeheap ranges still exist).
        AssertGenerationContainsItsObjects(target, ee, GcGeneration.Gen0, requireNonEmpty: true);
        AssertGenerationContainsItsObjects(target, ee, GcGeneration.Gen1, requireNonEmpty: false);
        AssertGenerationContainsItsObjects(target, ee, GcGeneration.Gen2, requireNonEmpty: false);
        AssertGenerationContainsItsObjects(target, ee, GcGeneration.Loh, requireNonEmpty: true);
        if (config.Flavor != Flavor.Framework)
        {
            AssertGenerationContainsItsObjects(target, ee, GcGeneration.Poh, requireNonEmpty: true);
        }

        // --- -segment <addr>: a populated SOH segment's objects all live within its [begin, allocated).
        //     Regions layout splits the SOH per generation (Gen0/1/2); the segment/ephemeral layout keeps
        //     them in a single Soh list — gather both so the selection is format-agnostic. ---
        EeHeapSegment segment = ee.Heaps
            .SelectMany(h => h.Gen0.Concat(h.Gen1).Concat(h.Gen2).Concat(h.Soh))
            .First(s => s.Allocated > s.Begin);
        IReadOnlyList<ulong> inSegment = target.DumpHeap($"-segment {segment.Segment:x} -short").ShortAddresses;
        Assert.NotEmpty(inSegment);
        Assert.All(inSegment, a => Assert.True(a >= segment.Begin && a < segment.Allocated,
            $"object 0x{a:x} within segment [0x{segment.Begin:x}, 0x{segment.Allocated:x})"));

        // --- positional [start end]: limiting to a populated gen0 range returns objects, all within it. ---
        (ulong start, ulong end) = ee.GenerationRanges(GcGeneration.Gen0).First(r => r.End > r.Start);
        IReadOnlyList<ulong> inRange = target.DumpHeap($"{start:x} {end:x} -short").ShortAddresses;
        Assert.NotEmpty(inRange);
        Assert.All(inRange, a => Assert.True(a >= start && a < end, $"object 0x{a:x} within [0x{start:x}, 0x{end:x})"));

        // --- -startAtLowerBound: legacy flag that survives only in the help text. The modern
        //     command-based dumpheap has no such [Option], so SOS rejects it ("Unknown argument")
        //     rather than silently walking the heap. Assert the rejection so the gap stays documented
        //     and a future re-implementation flips this test loudly.
        SosOutput lowerBound = target.Sos($"dumpheap {start:x} {end:x} -startAtLowerBound");
        lowerBound.AssertContains("Unknown argument");

        // --- -heap <n>: heap 0 is a non-empty subset of the whole heap. ---
        IReadOnlyList<ulong> heap0 = target.DumpHeap("-heap 0 -short").ShortAddresses;
        Assert.NotEmpty(heap0);
        IReadOnlyList<ulong> all = target.DumpHeap("-short").ShortAddresses;
        Assert.Subset(all.ToHashSet(), heap0.ToHashSet());
    }

    private static void AssertGenerationContainsItsObjects(Target target, EeHeap ee, GcGeneration generation, bool requireNonEmpty)
    {
        IReadOnlyList<(ulong Start, ulong End)> ranges = ee.GenerationRanges(generation);
        Assert.NotEmpty(ranges); // eeheap must have parsed a real layout for this generation

        IReadOnlyList<ulong> objects = target.DumpHeap($"-gen {GenArg(generation)} -short").ShortAddresses;
        if (requireNonEmpty)
        {
            Assert.NotEmpty(objects); // guard: a silent empty dumpheap must not pass
        }

        Assert.All(objects, a => Assert.True(
            ranges.Any(r => a >= r.Start && a < r.End),
            $"object 0x{a:x} falls in a {generation} range"));
    }

    private static string GenArg(GcGeneration generation) => generation switch
    {
        GcGeneration.Gen0 => "gen0",
        GcGeneration.Gen1 => "gen1",
        GcGeneration.Gen2 => "gen2",
        GcGeneration.Loh => "loh",
        GcGeneration.Poh => "poh",
        _ => throw new ArgumentOutOfRangeException(nameof(generation)),
    };
}
