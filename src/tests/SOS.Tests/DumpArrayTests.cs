// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// <c>!dumparray</c> and all of its parameters (<c>-start</c>, <c>-length</c>, <c>-details</c>,
/// <c>-nofields</c>) on the debuggee's known <c>int[]</c> (hung off <c>FieldMarker.Numbers</c> so it is
/// found by name, not by a fragile heap scan). The array's contents are deterministic — element
/// <c>i == (i + 1) * KnownIntArrayElementStep</c> — so <c>-details</c> can be checked against exact values.
/// The negative/edge behaviours documented in <c>strike.cpp</c> (the <c>-nofields</c>-without-<c>-details</c>
/// warning, "Not an array", and "Start index out of range") are exercised too.
/// </summary>
public sealed class DumpArrayTests
{
    public static TheoryData<TestConfig> Matrix => TestMatrices.CoreFrameworkConditional([TargetCatalog.Scenarios]);

    private static readonly Regex s_elementValue = new(@"(\d+)\s+m_value", RegexOptions.Compiled);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpArray_StructureStartLengthDetails(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong array = ArrayAddress(target);
        int length = TestTargets.SosHarnessScenarios.KnownIntArrayLength;
        int step = TestTargets.SosHarnessScenarios.KnownIntArrayElementStep;

        // Structure: single-rank Int32 array with the expected element count.
        DumpArrayResult full = target.DumpArray(array);
        Assert.Equal(1, full.Rank);
        Assert.Equal(length, full.NumberOfElements);
        Assert.Equal("Int32", full.ElementType);
        Assert.Equal(length, full.Elements.Count);

        // -start/-length window: exactly indices [2,3,4] are listed.
        DumpArrayResult window = target.DumpArray(array, start: 2, length: 3);
        Assert.Equal(new[] { 2, 3, 4 }, window.Elements.Select(e => e.Index));

        // -details: each windowed element's value class (System.Int32) is dumped with its m_value, which
        // must equal the known content (i + 1) * step.
        DumpArrayResult details = target.DumpArray(array, start: 2, length: 3, details: true);
        int[] values = s_elementValue.Matches(details.Output.Text).Select(m => int.Parse(m.Groups[1].Value)).ToArray();
        Assert.Equal(new[] { 3 * step, 4 * step, 5 * step }, values);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpArray_ParameterEdgeCases(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong array = ArrayAddress(target);

        // -nofields only takes effect with -details; on its own SOS warns and does nothing.
        target.Sos($"dumparray -nofields {array:x}")
            .AssertContains("-nofields has no effect unless -details is specified");

        // Pointed at a non-array object, dumparray redirects you to dumpobj.
        ulong notArray = target.FindUniqueObject("FieldMarker");
        target.Sos($"dumparray {notArray:x}").AssertContains("Not an array");

        // A start index past the end is rejected.
        target.Sos($"dumparray -start 99999 {array:x}").AssertContains("Start index out of range");
    }

    private static ulong ArrayAddress(Target target)
    {
        DumpObjResult holder = target.DumpObj(target.FindUniqueObject("FieldMarker"));
        return ObjectCommandParsing.Hex(holder.Field("Numbers").Value);
    }
}
