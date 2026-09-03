// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for <c>!dumpheap -live</c> / <c>-dead</c>. The <see cref="TargetCatalog.Scenarios"/>
/// debuggee deliberately holds one rooted (LIVE) object and one unreachable-but-uncollected (DEAD)
/// object, each uniquely typed, so the live/dead partition is checkable by type: the live marker shows
/// under <c>-live</c> and not <c>-dead</c>; the dead marker is the opposite; and passing both flags
/// cancels (SOS clears them) so the result is the unfiltered set.
/// </summary>
public sealed class DumpHeapLiveDeadTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    private const string LiveType = "LiveUniqueMarker";
    private const string DeadType = "DeadUniqueMarker";

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpHeap_LiveAndDead(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        // Baseline: both objects are on the heap (the unfiltered walk sees live and dead alike).
        Assert.Equal(1, target.DumpHeap($"-type {LiveType}").CountOf(LiveType));
        Assert.Equal(1, target.DumpHeap($"-type {DeadType}").CountOf(DeadType));

        // The rooted marker is live: present under -live, absent under -dead.
        Assert.Equal(1, target.DumpHeap($"-type {LiveType} -live").CountOf(LiveType));
        Assert.Equal(0, target.DumpHeap($"-type {LiveType} -dead").CountOf(LiveType));

        // The unreachable marker is dead: present under -dead, absent under -live.
        Assert.Equal(1, target.DumpHeap($"-type {DeadType} -dead").CountOf(DeadType));
        Assert.Equal(0, target.DumpHeap($"-type {DeadType} -live").CountOf(DeadType));

        // -live -dead together cancel (SOS clears both) -> the unfiltered set.
        Assert.Equal(1, target.DumpHeap($"-type {LiveType} -live -dead").CountOf(LiveType));
        Assert.Equal(1, target.DumpHeap($"-type {DeadType} -live -dead").CountOf(DeadType));
    }
}
