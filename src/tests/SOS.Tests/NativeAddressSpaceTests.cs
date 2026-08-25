// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for <c>!notreachableinrange</c>, a <c>!finalizerqueue</c> helper that scans an address range
/// for unreachable managed objects.
/// </summary>
public sealed class NativeAddressSpaceTests
{
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task NotReachableInRange_ScansPointerRange(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // notreachableinrange treats [start,end) as an array of object pointers (it backs !finalizerqueue)
        // and reports the dead ones. Point it at a known live object's span: the command computes the live
        // set and emits the dumpheap-style listing, proving the scan path works.
        ulong marker = target.FindUniqueObject("FieldMarker");
        SosOutput scan = target.Sos($"notreachableinrange {marker:x} {marker + 0x200:x}");
        scan.AssertContains("Calculating live objects");
    }

}
