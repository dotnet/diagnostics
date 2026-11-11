// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Whole-heap analysis commands: <c>!sizestats</c> (per-generation size histogram), <c>!traverseheap</c>
/// (writes the heap graph to a CLR-Profiler file), and the ephemeral-reference scans <c>!ephrefs</c> /
/// <c>!ephtoloh</c>. (The native memory-region commands <c>!maddress</c>/<c>!gctonative</c>/
/// <c>!notreachableinrange</c> lives in <see cref="NativeAddressSpaceTests"/>.)
/// </summary>
public sealed class HeapAnalysisTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task SizeStats_ReportsGenerationHistogram(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("sizestats").AssertContains("Size Statistics");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task TraverseHeap_WritesProfilerFile(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        string file = Path.Combine(Path.GetTempPath(), $"traverseheap-{Guid.NewGuid():N}.out");
        try
        {
            target.Sos($"traverseheap {file}");
            Assert.True(File.Exists(file), $"traverseheap should have written {file}");
            Assert.True(new FileInfo(file).Length > 0, "the profiler file should be non-empty");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task EphemeralScans_Run(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // ephrefs/ephtoloh are managed extension commands (dotnet-dump only).
        target.Sos("ephrefs").AssertContains("References from");
        target.Sos("ephtoloh").AssertContains("Ephemeral");
    }
}
