// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// GC / heap inspection commands the legacy <c>.script</c> suite exercised but the modern harness did not:
/// <c>!gcroot</c>, <c>!objsize</c>, <c>!gchandles</c>, <c>!finalizequeue</c>, <c>!gcheapstat</c>,
/// <c>!verifyheap</c>, and <c>!dumpgen</c>. Each is anchored on the debuggee's known objects (the rooted
/// live marker, the dropped dead marker, and the field-rich <c>FieldMarker</c>) so the assertions are real
/// oracles, not just "the command ran".
/// </summary>
public sealed class GcInspectionTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);
    public static TheoryData<TestConfig> PohMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.Core | Flavor.SingleFile);
    public static TheoryData<TestConfig> DumpGenMatrix => TestConfig.BuildMatrix(
        [TargetCatalog.DumpGen], Flavor.Core | Flavor.SingleFile, gcType: GcType.AllValid);
    public static TheoryData<TestConfig> FindRootsMatrix => TestConfig.BuildMatrix(
        [TargetCatalog.FindRootsOlderGeneration],
        Flavor.Core,
        Host.Cdb | Host.Lldb,
        Liveness.Live,
        GcType.AllValid,
        coreVersion: CoreVersion.All & ~(CoreVersion.Net8 | CoreVersion.Net9),
        dac: Dac.Legacy);

    [SosTheory]
    [MemberData(nameof(PohMatrix))]
    public async Task GcRoot_FindsRootsForPohObject(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        string module = TargetCatalog.Get(config.Target).ModuleFor(config.Flavor);
        EEMatch type = Assert.Single(target.Name2EE($"{module}!SosHarnessScenarios").Matches, match => match.Name == "SosHarnessScenarios");
        Assert.NotNull(type.MethodTable);
        DumpMtResult methodTable = target.DumpMt(type.MethodTable.Value);
        SosTable fields = target.DumpClass(methodTable.EEClass ?? type.MethodTable.Value).Output.Table(
            ColumnAlignment.Right("MT"), ColumnAlignment.Right("Field"), ColumnAlignment.Right("Offset"),
            ColumnAlignment.Right("Type"), ColumnAlignment.Right("VT"), ColumnAlignment.Right("Attr"), ColumnAlignment.Right("Value"), "Name");
        ulong poh = fields.SingleRow(row => row["Name"] == "s_poh", "the rooted POH array")["Value"].AsUInt64(Sos.Addr);
        Assert.NotEqual(0ul, poh);

        DumpArrayResult array = target.DumpArray(poh, length: 0);
        Assert.Equal("System.Byte[]", array.Name);
        Assert.Equal(TestTargets.SosHarnessScenarios.PohArraySize, array.NumberOfElements);

        SosTable locations = target.Sos($"gcwhere {poh:x}").Table(
            "Address", "Heap", "Segment", "Generation", "Allocated", "Committed", "Reserved");
        SosRow location = locations.SingleRow(row => row["Address"].AsUInt64(Sos.Addr) == poh, $"the POH array at {poh:x}");
        Assert.Equal("pinned", location["Generation"]);

        SosOutput roots = target.Sos($"gcroot {poh:x}");
        Assert.True(UniqueRootCount(roots) > 0, $"expected roots for the POH array:\n{roots.Text}");
        roots.AssertContains(poh.ToString("x"));
        roots.AssertContains("System.Byte[]");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task GcRoot_FindsRootsForLive_NoneForDead(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // The live marker is rooted by a static, so gcroot finds at least one root and names the object.
        ulong live = target.FindUniqueObject("LiveUniqueMarker");
        SosOutput liveRoots = target.Sos($"gcroot {live:x}");
        liveRoots.AssertContains("LiveUniqueMarker");
        Assert.True(UniqueRootCount(liveRoots) > 0, $"expected roots for the live marker:\n{liveRoots.Text}");

        // The dead marker is unreachable (only uncollected), so gcroot finds no roots.
        ulong dead = target.FindUniqueObject("DeadUniqueMarker");
        SosOutput deadRoots = target.Sos($"gcroot {dead:x}");
        Assert.Equal(0, UniqueRootCount(deadRoots));
    }

    [SosTheory(SkipTestWithoutData = true)]
    [MemberData(nameof(FindRootsMatrix))]
    public async Task FindRoots_SearchesOlderGenerations(TestConfig config)
    {
        // Regression for https://github.com/dotnet/runtime/pull/119396:
        // findroots must search older generations for references to surviving young objects.
        SOSTestSkips.SkipFindRootsNotifications(config);
        using LiveTarget target = (LiveTarget)await Targets.GetTargetAsync(config);
        target.GoToStopPoint("ready");

        // The LOH array references a gen0 Thing, so a gen0 collection must preserve that older-generation root.
        EeHeap heap = target.EeHeap();
        Assert.Equal(config.GcType == GcType.Server, heap.HeapCount > 1);
        SosTable stackObjects = target.DumpStackObjects();
        SosRow array = stackObjects
            .First(row => row["Name"].Value == "FindRootsOlderGeneration.Thing[]");
        ulong arrayAddress = array["Object"].AsUInt64(Sos.Addr);
        Assert.Contains(heap.GenerationRanges(GcGeneration.Loh), range => arrayAddress >= range.Start && arrayAddress < range.End);
        ulong youngAddress = stackObjects.First(row => row["Name"].Value == "FindRootsOlderGeneration.Thing")["Object"].AsUInt64(Sos.Addr);
        Assert.Contains(heap.GenerationRanges(GcGeneration.Gen0), range => youngAddress >= range.Start && youngAddress < range.End);

        // Request GC notifications and resume to the end of marking, before the collection completes.
        // findroots installs the debugger's notification handler itself.
        target.Sos("findroots -gen any");
        target.RunToBreakpoint();

        // Server GC can report the notification on a GC worker; inspect the main thread's local objects.
        TargetExtensions.ThreadStack mainThread = Assert.Single(target.ClrstackAllThreads(),
            thread => thread.Frames.Any(frame => frame.Function.Contains("FindRootsOlderGeneration.Program.Main", StringComparison.Ordinal)));
        ulong osThreadId = Convert.ToUInt64(mainThread.OsThreadId, 16);
        string debuggerThreadId = target.Sos("clrthreads").AsThreadsTable()
            .SingleRow(row => row["OSID"].AsUInt64(Sos.Addr) == osThreadId, "the main managed thread")["DBG"].Value;
        target.Execute(config.Host == Host.Cdb ? $"~{debuggerThreadId}s" : $"thread select {debuggerThreadId}");

        SosRow thing = target.DumpStackObjects()
            .First(row => row["Name"].Value == "FindRootsOlderGeneration.Thing");
        ulong address = thing["Object"].AsUInt64(Sos.Addr);
        // The unique root must include the LOH array, not just find the Thing directly on the stack.
        SosOutput roots = target.Sos($"findroots {address:x}");
        Assert.Equal(1, UniqueRootCount(roots));
        roots.AssertContains("FindRootsOlderGeneration.Thing[]");
        roots.AssertContains(arrayAddress.ToString("x"));
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task ObjSize_CountsTransitiveClosure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // The fieldless live marker keeps only itself alive.
        ulong live = target.FindUniqueObject("LiveUniqueMarker");
        (int liveCount, _) = ObjSizeTotal(target.Sos($"objsize {live:x}"));
        Assert.Equal(1, liveCount);

        // FieldMarker keeps its string + int[] + signature byte[]s alive, so its closure is strictly bigger.
        ulong fields = target.FindUniqueObject("FieldMarker");
        (int fieldCount, long fieldBytes) = ObjSizeTotal(target.Sos($"objsize {fields:x}"));
        Assert.True(fieldCount > liveCount, $"FieldMarker closure ({fieldCount}) should exceed the marker's ({liveCount})");
        Assert.True(fieldBytes > 0);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task GcHandles_ReportsHandleSummary(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput handles = target.Sos("gchandles -stat");
        handles.AssertContains("Handles:");
        Match strong = Regex.Match(handles.Text, @"Strong Handles:\s+(\d+)");
        Assert.True(strong.Success && int.Parse(strong.Groups[1].Value) > 0, $"expected strong handles:\n{handles.Text}");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task FinalizeQueue_ReportsStructure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput fq = target.Sos("finalizequeue -stat");
        fq.AssertContains("finalizable objects");
        Assert.Matches(@"generation 0 has \d+ objects", fq.Text);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task GcHeapStat_ReportsGenerationSizes(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput stat = target.Sos("gcheapstat");
        // The allocated-size table has a per-heap row; gen0 always holds the bulk of our fresh allocations.
        Match heap0 = Regex.Match(stat.Text, @"Heap0\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.Multiline);
        Assert.True(heap0.Success, $"expected a Heap0 row:\n{stat.Text}");
        Assert.True(long.Parse(heap0.Groups[1].Value) > 0, "gen0 allocated size should be positive");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task VerifyHeap_ReportsNoCorruption(TestConfig config)
    {
        if (RepoLayout.Rid.StartsWith("linux-musl-", StringComparison.Ordinal))
        {
            HarnessSkipException.Now(
                "The DAC reports incorrect sync-block-related verifyheap failures on Linux musl.");
        }

        if (OperatingSystem.IsMacOS() &&
            config.Host == Host.DotnetDump &&
            config.Dac == Dac.Legacy &&
            config.CoreVersion == CoreVersion.Net11)
        {
            HarnessSkipException.Now(
                "https://github.com/dotnet/diagnostics/issues/5985: legacy DAC SyncBlock data is unavailable for macOS .NET 11 dumps.");
        }

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopGen1);

        SosOutput verify = target.Sos("verifyheap");
        Assert.Matches(@"\b0 errors\b", verify.Text);
        verify.AssertContains("No heap corruption detected");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpGen_ListsGenerationObjects(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // dumpgen gen0 is the dedicated "objects in a generation" command; our thin-lock marker lives there.
        SosOutput gen0 = target.Sos("dumpgen gen0");
        gen0.AssertContains("ThinLockMarker");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpGen_ArgumentsAndFilters(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("dumpgen").AssertContains("Generation argument is missing");
        target.Sos("dumpgen invalid").AssertContains("invalid is not a supported generation");
        target.Sos("dumpgen gen0 -mt").AssertContains("Required argument missing for option: '-mt'.");
        target.Sos("dumpgen gen1 -mt zzzzz").AssertContains("Hexadecimal address expected for -mt option");

        DumpHeapResult markerDump = target.DumpHeap("-type ThinLockMarker");
        SosRow markerStat = markerDump.Statistics
            .SingleRow(r => r["Class Name"].Value == "ThinLockMarker", "the ThinLockMarker method table");
        Assert.Equal(1, markerStat["Count"].AsInt32(Sos.Integer));
        ulong methodTable = markerStat["MT"].AsUInt64(Sos.Addr);
        SosRow markerRow = markerDump.Objects
            .SingleRow(r => r["MT"].AsUInt64(Sos.Addr) == methodTable, "the ThinLockMarker object");
        ulong marker = markerRow["Address"].AsUInt64(Sos.Addr);

        target.Sos("dumpgen gen0 -type ThinLockMarker")
            .AssertContains("ThinLockMarker");
        target.Sos($"dumpgen gen0 -mt {methodTable:x}")
            .AssertContains(marker.ToString("x"));
    }

    [SosTheory]
    [MemberData(nameof(DumpGenMatrix))]
    public async Task DumpGen_ReportsExactGenerationPopulations(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint("ready");

        const string sampleType = "DotnetDumpCommands.Program+DumpSampleClass";
        const string sampleArrayType = sampleType + "[]";

        Assert.Equal(10, GenerationCount(target.Sos("dumpgen gen0"), sampleType));
        Assert.Equal(3, GenerationCount(target.Sos("dumpgen gen1"), sampleType));
        Assert.Equal(5, GenerationCount(target.Sos("dumpgen gen2"), sampleType));
        Assert.Equal(1, GenerationCount(target.Sos("dumpgen loh"), sampleArrayType));

        SosOutput poh = target.Sos("dumpgen poh");
        Assert.Equal(1, GenerationCount(poh, "System.Byte[]"));
        ulong byteArrayMethodTable = GenerationStatistics(poh)
            .SingleRow(row => row["Class Name"].Value == "System.Byte[]", "the pinned byte array")["MT"].AsUInt64(Sos.Addr);
        ulong pinnedArray = GenerationObjects(target.Sos($"dumpgen poh -mt {byteArrayMethodTable:x}"))
            .SingleRow(_ => true, "the pinned byte array")["Address"].AsUInt64(Sos.Addr);
        Assert.DoesNotContain(
            GenerationObjects(target.Sos($"dumpgen gen2 -mt {byteArrayMethodTable:x}")),
            row => row["Address"].AsUInt64(Sos.Addr) == pinnedArray);

        SosOutput gen0 = target.Sos("dumpgen gen0 -type DotnetDumpCommands");
        Assert.Equal(10, GenerationCount(gen0, sampleType));
        Assert.Equal(10, TotalObjects(gen0));

        ulong methodTable = GenerationStatistics(target.Sos("dumpgen gen0"))
            .SingleRow(row => row["Class Name"].Value == sampleType, sampleType)["MT"].AsUInt64(Sos.Addr);
        SosOutput byMethodTable = target.Sos($"dumpgen gen0 -mt {methodTable:x}");
        Assert.Equal(10, GenerationObjects(byMethodTable).Length);
        Assert.Equal(10, TotalObjects(byMethodTable));

        Assert.Equal(0, TotalObjects(target.Sos("dumpgen gen0 -mt 1")));
        Assert.Equal(0, TotalObjects(target.Sos("dumpgen gen0 -type NoMatchingType")));
    }

    private static SosTable GenerationStatistics(SosOutput output) => output.Table(
        ColumnAlignment.Right("MT"), ColumnAlignment.Right("Count"), ColumnAlignment.Right("TotalSize"), "Class Name");

    private static SosTable GenerationObjects(SosOutput output) => output.Table(
        ColumnAlignment.Right("Address"), ColumnAlignment.Right("MT"), ColumnAlignment.Right("Size"));

    private static int GenerationCount(SosOutput output, string typeName)
    {
        SosRow? row = GenerationStatistics(output).SingleOrDefault(row => row["Class Name"].Value == typeName);
        return row is null ? 0 : row["Count"].AsInt32(Sos.Integer);
    }

    private static int TotalObjects(SosOutput output)
    {
        Match match = Regex.Match(output.Text, @"Total\s+([\d,]+)\s+objects");
        Assert.True(match.Success, $"expected a dumpgen total:\n{output.Text}");
        return int.Parse(match.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
    }

    private static int UniqueRootCount(SosOutput output)
    {
        Match m = Regex.Match(output.Text, @"Found (\d+) unique roots");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    private static (int Count, long Bytes) ObjSizeTotal(SosOutput output)
    {
        Match m = Regex.Match(output.Text, @"Total ([\d,]+) objects,?\s*([\d,]+) bytes");
        Assert.True(m.Success, $"expected an objsize total:\n{output.Text}");
        return (int.Parse(m.Groups[1].Value.Replace(",", "")), long.Parse(m.Groups[2].Value.Replace(",", "")));
    }
}
