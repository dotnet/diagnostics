// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Snapshot multi-stop tests across the full host × flavor × stopPoint matrix. One debuggee run
/// per (flavor, target) produces gen0/gen1/gen2 dumps; each is an immutable, independently-loaded
/// stop point. ClrMD is the oracle: it finds the array and its generation, and we assert SOS's
/// <c>gcwhere</c> agrees — for .NET Core, single-file, and desktop .NET Framework alike.
/// </summary>
public sealed class GcWhereTests
{
    // gcwhere's structure check is dump-only (the generation layout is identical in a dump). The
    // generation-promotion check (GcWhere_Moves) is the live-worthy one: it drives bpmd through the
    // gen0->gen1->gen2 promotion (GC.Collect(2) between markers) on a live process, so it opts into live.
    public static TheoryData<TestConfig> Matrix => TestMatrices.CurrentThreadCommands([TargetCatalog.Scenarios]);
    public static TheoryData<TestConfig> LiveMatrix =>
        TestMatrices.CurrentThreadCommands([TargetCatalog.Scenarios], Liveness.AllValid);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task GcWhere_Structure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint("gen0");

        ulong obj = FindObject(target);
        SosOutput gcwhere = target.Sos($"gcwhere {obj:x}");

        SosTable table = gcwhere.Table(
            ("Address", Sos.Addr), ("Heap", Sos.Integer), ("Segment", Sos.Addr), ("Generation", Sos.Integer),
            ("Allocated", Sos.MemRange), ("Committed", Sos.MemRange), ("Reserved", Sos.MemRange));
        Assert.NotEmpty(table);
    }

    private static ulong FindObject(Target target)
    {
        SosTable dsoTable = target.DumpStackObjects();
        SosRow row = dsoTable.First(r => r["Name"] == "System.Int32[]");
        ulong obj = row["Object"].AsUInt64(Sos.Addr);
        return obj;
    }

    [SosTheory]
    [MemberData(nameof(LiveMatrix))]
    public async Task GcWhere_Moves(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);

        CheckGeneration(target, 0);
        CheckGeneration(target, 1);
        CheckGeneration(target, 2);
    }

    private static void CheckGeneration(Target target, int gen)
    {
        target.GoToStopPoint($"gen{gen}");
        ulong obj = FindObject(target);
        SosOutput gcwhere = target.Sos($"gcwhere {obj:x}");

        SosTable table = gcwhere.Table("Address", "Heap", "Segment", "Generation", "Allocated", "Committed", "Reserved");
        SosRow row = table.SingleRow(r => r["Address"].AsUInt64(Sos.Addr) == obj, $"a row whose Address is 0x{obj:x}");
        Assert.Equal(gen, row["Generation"].AsInt32(Sos.Integer));
    }
}
