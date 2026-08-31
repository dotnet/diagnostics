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

        // notreachableinrange treats [start,end) as an array of object pointers (it backs !finalizerqueue).
        // ObjectReference[] contains one-field value types, so dumparray reports the address of an
        // actual object-reference slot rather than the referenced object or an object header.
        ulong references = target.FindUniqueObject("ObjectReference[]");
        ulong slot = target.DumpArray(references).Elements[0].Address;

        // Warm the shared liveness cache explicitly. notreachableinrange has no output when every
        // pointer in the range is live, while a cold cache emits a non-contractual progress banner.
        target.Sos("dumpheap -type LiveUniqueMarker -live").AssertContains("LiveUniqueMarker");
        SosOutput scan = target.Sos($"notreachableinrange {slot:x} {slot + (ulong)IntPtr.Size:x}");
        Assert.True(string.IsNullOrWhiteSpace(scan.Text), $"expected no unreachable objects:\n{scan.Text}");
    }

}
