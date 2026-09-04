// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Per-object GC helper commands: <c>!dumpobjgcrefs</c>, <c>!listnearobj</c>, <c>!verifyobj</c>,
/// <c>!findappdomain</c>, <c>!dumpalc</c>, <c>!pathto</c>, and <c>!gchandleleaks</c>. All anchored on the
/// debuggee's reference-rich <c>FieldMarker</c> (it points at a string, an int[], and byte[]s), so the GC
/// references and object neighbours are known.
/// </summary>
public sealed class ObjectGcHelperTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);
    public static TheoryData<TestConfig> CoreRuntimeMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.Core | Flavor.SingleFile);
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);
    public static TheoryData<TestConfig> ObjectDataMatrix => TestMatrices.CoreFrameworkConditional([TargetCatalog.Scenarios]);

    // gchandleleaks is a Windows-only SOS command (gated #ifndef FEATURE_PAL); pair the Windows-only cdb
    // host matrix with [WindowsTheory] so off-Windows rows are never generated rather than skipped.
    //
    // Framework (desktop .NET) is excluded on purpose: gchandleleaks brute-force linear-scans the entire
    // committed virtual address space (dbgeng QueryVirtual/ReadVirtual, an inner O(handles) compare per word).
    // On a desktop Framework dump that scan is progressing but legitimately exceeds the 2-minute command
    // timeout (~2m per config, every Framework version — they are the same net48 process), whereas the Core and
    // SingleFile dumps complete in a few seconds. We keep the fast, representative Core/SingleFile coverage and
    // drop the pathologically slow Framework rows rather than let the suite carry multi-minute tests.
    public static TheoryData<TestConfig> CdbMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.Core | Flavor.SingleFile, Host.Cdb);

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task DumpObjGcRefs_ListsReferences(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // dumpobjgcrefs (the engine behind dumpobj -refs) is a managed extension command (dotnet-dump only).
        SosOutput refs = target.Sos($"dumpobjgcrefs {target.FindUniqueObject("FieldMarker"):x}");
        refs.AssertContains("TextField");
        refs.AssertContains("System.String");
        refs.AssertContains("Numbers");
        refs.AssertContains("System.Int32[]");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task ListNearObj_ShowsNeighbours(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong marker = target.FindUniqueObject("FieldMarker");
        SosOutput near = target.Sos($"listnearobj {marker:x}");
        near.AssertContains("Current:");
        Assert.Contains("FieldMarker", near.Text, StringComparison.Ordinal);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task VerifyObj_AcceptsGoodObject(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos($"verifyobj {target.FindUniqueObject("FieldMarker"):x}").AssertContains("is a valid object");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task FindAppDomain_ResolvesObjectDomain(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput domain = target.Sos($"findappdomain {target.FindUniqueObject("FieldMarker"):x}");
        domain.AssertContains("AppDomain:");
        domain.AssertContains("Name:");
    }

    [SosTheory]
    [MemberData(nameof(ObjectDataMatrix))]
    public async Task PathTo_TracesReferencePath(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong marker = target.FindUniqueObject("FieldMarker");
        ulong text = ObjectCommandParsing.Hex(target.DumpObj(marker).Field("TextField").Value);

        // FieldMarker references its TextField string directly, so the GC path goes marker -> string.
        SosOutput path = target.Sos($"pathto {marker:x} {text:x}");
        Assert.Contains(marker.ToString("x"), path.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System.String", path.Text, StringComparison.Ordinal);
    }

    [SosTheory]
    [MemberData(nameof(CoreRuntimeMatrix))]
    public async Task DumpAlc_ResolvesDefaultLoadContext(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // AssemblyLoadContext is a .NET Core concept; the debuggee loads into the default ALC.
        target.Sos($"dumpalc {target.FindUniqueObject("FieldMarker"):x}").AssertContains("DefaultAssemblyLoadContext");
    }

    [WindowsTheory]
    [MemberData(nameof(CdbMatrix))]
    public async Task GcHandleLeaks_RunsHandleScan(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("gchandleleaks").AssertContains("GCHandle");
    }
}
