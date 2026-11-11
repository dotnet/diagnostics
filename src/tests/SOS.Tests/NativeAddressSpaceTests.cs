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

        SosOutput scan = target.Sos($"notreachableinrange {slot:x} {slot + (ulong)IntPtr.Size:x}");
        string[] results = scan.Lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !IsLivenessProgress(line))
            .ToArray();
        Assert.Empty(results);
    }

    internal static bool IsLivenessProgress(string line) =>
        line.StartsWith("Calculating live objects", StringComparison.Ordinal) ||
        line is "Caching GC roots, this may take a while." or
            "Subsequent runs of this command will be faster.";
}
