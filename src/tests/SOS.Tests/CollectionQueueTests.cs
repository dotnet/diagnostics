// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// The concurrent-collection and thread-pool-queue dumpers: <c>!dumpconcurrentqueue</c>/<c>!dcq</c> and
/// <c>!threadpoolqueue</c>/<c>!tpq</c>. Both are managed extension commands (dotnet-dump only). The debuggee
/// stages a <c>ConcurrentQueue&lt;int&gt;</c> with known values so dcq's contents are verifiable.
/// </summary>
public sealed class CollectionQueueTests
{
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task Dcq_DumpsConcurrentQueueContents(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong queue = target.FirstObjectOfExactType("System.Collections.Concurrent.ConcurrentQueue<System.Int32>");
        SosOutput dcq = target.Sos($"dcq {queue:x}");
        dcq.AssertContains("ConcurrentQueue<System.Int32>");
        // The debuggee enqueued 0x111, 0x222, 0x333 (273, 546, 819 in decimal).
        dcq.AssertContains("273");
        dcq.AssertContains("546");
        dcq.AssertContains("819");
    }

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task ThreadPoolQueue_ShowsQueueStructure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("threadpoolqueue").AssertContains("work item queue");
    }
}
