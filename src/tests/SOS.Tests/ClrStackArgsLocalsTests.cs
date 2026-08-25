// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for <c>!clrstack -p</c> (parameters), <c>-l</c> (locals) and <c>-a</c> (both). The legacy
/// scripts only ever ran <c>-a</c> (and transitively covered <c>-p</c>/<c>-l</c>); here all three are
/// exercised from one stop point. Correctness comes from cross-variant self-consistency — <c>-a</c>'s
/// per-frame PARAMETERS equal <c>-p</c>'s and its LOCALS equal <c>-l</c>'s, while <c>-p</c> shows no
/// locals and <c>-l</c> no parameters — plus sensible value hardcoding and an SOS-native value oracle:
/// a uniquely-typed object's slot value equals its <c>!dumpheap</c> address.
/// </summary>
public sealed class ClrStackArgsLocalsTests
{
    public static TheoryData<TestConfig> Matrix { get; }
        = TestMatrices.StackWalkFullDumpOnCoreVersions(
            [
                TargetCatalog.DivZero,
                TargetCatalog.Scenarios,
            ],
            CoreVersion.Net8 | CoreVersion.Net9 | CoreVersion.Net10);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task ClrStack_ArgsLocals(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        if (config.Target == TargetCatalog.Scenarios)
        {
            target.GoToStopPoint(TargetCatalog.StopArgsLocals);
        }
        else
        {
            target.GoToFirstStop();
        }

        SosTable p = target.ClrstackArgsLocals(ArgsLocals.Parameters);
        SosTable l = target.ClrstackArgsLocals(ArgsLocals.Locals);
        SosTable a = target.ClrstackArgsLocals(ArgsLocals.Both);

        // Same stackwalk -> same frames in all three.
        Assert.Equal(a.Length, p.Length);
        Assert.Equal(a.Length, l.Length);

        // -a == -p (parameters) merged with -l (locals), frame by frame; -p has no locals and -l no
        // parameters.
        for (int i = 0; i < a.Length; i++)
        {
            SosRow fa = a.Row(i);
            SosRow fp = p.Row(i);
            SosRow fl = l.Row(i);
            Assert.Equal(fa["IP"].Value, fp["IP"].Value);
            Assert.Equal(fa["IP"].Value, fl["IP"].Value);

            Assert.Equal(Values(fa, "PARAMETERS"), Values(fp, "PARAMETERS"));
            Assert.Equal(Values(fa, "LOCALS"), Values(fl, "LOCALS"));
            Assert.Empty(Records(fp, "LOCALS"));
            Assert.Empty(Records(fl, "PARAMETERS"));
        }

        // At least one frame actually had parameters and one had locals (the merge above is meaningful).
        a.AssertContainsRow(f => Records(f, "PARAMETERS").Any(), "a frame has PARAMETERS");
        a.AssertContainsRow(f => Records(f, "LOCALS").Any(), "a frame has LOCALS");

        switch (config.Target)
        {
            case TargetCatalog.DivZero:
                // F3's locals are a=1, b=2 (ref-passed, so on the stack); F2's are p=3, q=4.
                AssertLocalValues(a, ".F3(", [1, 2]);
                AssertLocalValues(a, ".F2(", [3, 4]);
                break;

            case TargetCatalog.Scenarios:
                // The named int param `number` is 0x2a; a local holds the primitive 0x63.
                SosRow method = Frame(a, ".ArgsLocalsMethod(");
                Assert.Equal(0x2aul, Named(method, "number").AsUInt64(Sos.Hex));
                Records(method, "LOCALS").AssertContains(r => r["Value"].AsUInt64(Sos.Hex) == 0x63, "a LOCAL with value 0x63");

                // SOS-native value oracle: the uniquely-typed arg and local slots hold the very objects
                // !dumpheap finds for those types.
                ulong argObj = target.FindUniqueObject("ArgUniqueMarker");
                ulong localObj = target.FindUniqueObject("LocalUniqueMarker");
                Assert.Equal(argObj, Named(method, "arg").AsUInt64(Sos.Hex));
                Records(method, "LOCALS").AssertContains(r => r["Value"].AsUInt64(Sos.Hex) == localObj, "a LOCAL referencing the LocalUniqueMarker object");
                break;

            default:
                // A matrix target with no value assertions would otherwise silently pass on the
                // structural checks alone; force adding a case when a target is added.
                throw new ArgumentOutOfRangeException(nameof(config.Target), config.Target, "No clrstack args/locals value assertions defined for this target.");
        }
    }

    private static IEnumerable<SosDataRow> Records(SosRow frame, string section) =>
        frame.Data.Where(d => d["Section"].Value == section);

    private static IReadOnlyList<string> Values(SosRow frame, string section) =>
        Records(frame, section).Select(r => r["Value"].Value).ToList();

    private static SosRow Frame(SosTable table, string functionSubstring) =>
        table.SingleRow(
            r => !r["InternalFrame"].AsBoolean() && r["Function"].Value.Contains(functionSubstring, StringComparison.Ordinal),
            $"a managed frame whose Function contains '{functionSubstring}'");

    // The parameter named <paramref name="name"/> in this frame.
    private static SosCell Named(SosRow frame, string name) =>
        Records(frame, "PARAMETERS").AssertSingle(r => r["Name"].Value == name, $"a PARAMETER named '{name}'")["Value"];

    private static void AssertLocalValues(SosTable table, string functionSubstring, int[] expected)
    {
        SosRow frame = Frame(table, functionSubstring);
        List<ulong> locals = Records(frame, "LOCALS").Where(r => r["HasData"].AsBoolean()).Select(r => r["Value"].AsUInt64(Sos.Hex)).ToList();
        foreach (int e in expected)
            Assert.Contains((ulong)e, locals);
    }
}
