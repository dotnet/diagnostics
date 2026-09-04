// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for <c>!dumpheap -thinlock</c>. The Scenarios debuggee's <c>thinlock</c> stop is taken while a
/// dedicated thread holds an uncontended <c>Monitor</c> (lock) on a uniquely-typed <c>ThinLockMarker</c>
/// object — so the object carries a THIN lock (owning thread id stamped in the header), not a sync block.
/// The output table (<c>Object Thread OSId Recursion</c>) must list exactly that object with a real owning
/// thread and OS thread id; cross-checked against the object's <c>!dumpheap -type</c> address (the SOS-native
/// oracle).
/// </summary>
public sealed class DumpHeapThinLockTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpHeap_ThinLock(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopThinLock);

        // The uniquely-typed object the LockHolder thread holds a Monitor on.
        ulong lockObj = target.FindUniqueObject("ThinLockMarker");

        DumpHeapResult result = target.DumpHeap("-thinlock");
        Assert.True(result.HasThinLocks, $"expected a -thinlock table, got:\n{result.Output.Text}");
        SosTable locks = result.ThinLocks;
        Assert.NotEmpty(locks);

        // The ThinLockMarker object is reported with a real owning thread and OS thread id.
        SosRow row = locks.SingleRow(
            r => r["Object"].AsUInt64(Sos.Addr) == lockObj, $"a thinlock row for object 0x{lockObj:x}");
        Assert.NotEqual(0ul, row["Thread"].AsUInt64(Sos.Addr));
        Assert.NotEqual(0u, row["OSId"].AsUInt32(Sos.Hex));

        // A single, non-recursive lock: recursion count is 0 (no extra re-entrant acquisitions).
        Assert.Equal(0, row["Recursion"].AsInt32(Sos.Integer));
    }
}
