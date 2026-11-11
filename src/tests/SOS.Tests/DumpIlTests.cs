// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// <c>!dumpil</c> (MSIL of a managed method), on both hosts. The debuggee's <c>AtHeap()</c> has a tiny,
/// known body — load the "heap" literal, call <c>TestHarness.Stop</c>, return — so the decoded IL can be
/// asserted exactly. The <c>-i</c> form (decode from a raw IL pointer) is exercised by feeding back the IL
/// address dumpil itself reports, and the documented empty-argument error is checked.
/// </summary>
public sealed class DumpIlTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpIl_DecodesKnownMethod(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);
        EEMatch atHeap = Method(target, config.Flavor, "AtHeap");

        DumpIlResult il = target.DumpIl(atHeap.MethodDesc!.Value);
        Assert.NotEmpty(il.Instructions);
        Assert.NotEqual(0ul, il.IlAddress);

        // AtHeap's body is `TestHarness.Stop("heap"); return;` => ldstr / call / nop / ret. Assert the
        // opcodes and offsets (deterministic); operands print resolved names or raw metadata tokens
        // depending on metadata availability, so they're not asserted exactly.
        Assert.Equal(new[] { "ldstr", "call", "nop", "ret" }, il.Instructions.Select(i => i.OpCode));
        Assert.Equal(new[] { 0, 5, 10, 11 }, il.Instructions.Select(i => i.Offset));

        // -i decodes the same IL straight from the address dumpil reported.
        DumpIlResult byPointer = target.DumpIl(il.IlAddress, ilPointer: true);
        Assert.Equal(new[] { "ldstr", "call", "nop", "ret" }, byPointer.Instructions.Select(i => i.OpCode));
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpIl_RejectsEmptyExpression(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("dumpil 0").AssertContains("Must pass a valid expression");
    }

    private static EEMatch Method(Target target, Flavor flavor, string name)
    {
        string module = TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(flavor);
        return target.Name2EE($"{module}!SosHarnessScenarios.{name}").Single;
    }
}
