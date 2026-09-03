// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// <c>!clru</c> (alias of <c>!u</c>) annotated disassembly and all of its parameters. clru depends on the
/// debugger's native disassembler, which only the dbgeng (cdb) host provides — dotnet-dump reports it as an
/// unrecognized command — so this runs on a cdb-only matrix. The disassembly is architecture-specific, so
/// the assertions are structural (the banner, method name, <c>Begin/size</c>, a non-empty instruction list,
/// source-line presence) plus the interleaving that <c>-gcinfo</c>/<c>-ehinfo</c>/<c>-il</c> add — which is
/// cross-checked against the standalone <c>!gcinfo</c>/<c>!ehinfo</c> output.
/// </summary>
public sealed class ClrUTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.Cdb);

    // !clru's IL/source-line/EH interleaving relies on per-method debug info the DAC only sees in a Full
    // dump on net8-net10 (present in Heap dumps from net11 on); capture Full there. AcceptsInstructionPointer
    // needs none of that, so it stays on the default Heap Matrix.
    public static TheoryData<TestConfig> FullDumpMatrix => TestMatrices.FullDumpOnCoreVersions([TargetCatalog.Scenarios], CoreVersion.Net8 | CoreVersion.Net9 | CoreVersion.Net10, Flavor.AllValid, Host.Cdb);

    [WindowsTheory]
    [MemberData(nameof(FullDumpMatrix))]
    public async Task ClrU_StructureLinesOffsets(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);
        EEMatch atHeap = Method(target, config.Flavor, "AtHeap");

        // Default: banner, method name, Begin/size, a real instruction stream, and source annotations.
        ClrUResult plain = target.ClrU(atHeap.MethodDesc!.Value);
        Assert.True(plain.HasNormalJitBanner);
        Assert.Contains("AtHeap", plain.MethodName, StringComparison.Ordinal);
        Assert.NotEqual(0ul, plain.Begin);
        Assert.True(plain.Size > 0);
        Assert.NotEmpty(plain.Instructions);
        Assert.True(plain.SourceLineCount > 0, "expected source-line annotations by default");
        Assert.False(plain.HasOffsets);

        // -n suppresses the source annotations but still disassembles.
        ClrUResult noLines = target.ClrU(atHeap.MethodDesc.Value, noLines: true);
        Assert.Equal(0, noLines.SourceLineCount);
        Assert.NotEmpty(noLines.Instructions);

        // -o prefixes every instruction with its offset.
        ClrUResult offsets = target.ClrU(atHeap.MethodDesc.Value, offsets: true);
        Assert.True(offsets.HasOffsets);
        Assert.Equal(0, offsets.Instructions[0].Offset);
    }

    [WindowsTheory]
    [MemberData(nameof(FullDumpMatrix))]
    public async Task ClrU_InterleavesGcInfoEhInfoIl(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);
        EEMatch atHeap = Method(target, config.Flavor, "AtHeap");
        EEMatch lockHolder = Method(target, config.Flavor, "LockHolder");

        // -il interleaves the MSIL.
        Assert.Contains("IL_", target.ClrU(atHeap.MethodDesc!.Value, il: true).Output.Text, StringComparison.Ordinal);

        // -gcinfo interleaves the same interruptibility info that standalone gcinfo prints.
        ClrUResult withGc = target.ClrU(atHeap.MethodDesc.Value, gcInfo: true);
        Assert.Contains("interruptible", withGc.Output.Text, StringComparison.Ordinal);
        Assert.NotEmpty(withGc.Instructions); // still a real disassembly, not just the gc dump
        Assert.NotEmpty(target.GcInfo(atHeap.MethodDesc.Value).Transitions);

        // -ehinfo interleaves the EH clause markers of a method that has a (finally) handler.
        ClrUResult withEh = target.ClrU(lockHolder.MethodDesc!.Value, ehInfo: true);
        Assert.Contains("EHHandler", withEh.Output.Text, StringComparison.Ordinal);
        Assert.Contains("FINALLY", withEh.Output.Text, StringComparison.Ordinal);
        Assert.Contains(target.EhInfo(lockHolder.MethodDesc.Value).Clauses, c => c.Kind.Contains("FINALLY", StringComparison.Ordinal));
    }

    [WindowsTheory]
    [MemberData(nameof(Matrix))]
    public async Task ClrU_AcceptsInstructionPointer(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);
        EEMatch atHeap = Method(target, config.Flavor, "AtHeap");

        // clru disassembles the same method whether given its MethodDesc or an IP inside it.
        ClrUResult byMd = target.ClrU(atHeap.MethodDesc!.Value);
        ClrUResult byIp = target.ClrU(atHeap.JittedCodeAddress!.Value);
        Assert.Equal(byMd.Begin, byIp.Begin);
        Assert.Equal(byMd.MethodName, byIp.MethodName);
    }

    private static EEMatch Method(Target target, Flavor flavor, string name)
    {
        string module = TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(flavor);
        return target.Name2EE($"{module}!SosHarnessScenarios.{name}").Single;
    }
}
