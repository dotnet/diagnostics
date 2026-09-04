// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// The standalone code-metadata commands <c>!ehinfo</c> and <c>!gcinfo</c>, on both hosts. Both accept a
/// MethodDesc or an instruction pointer; the tests anchor on a simple jitted method (<c>AtHeap</c>) and a
/// method whose <c>lock</c> lowers to a try/finally (<c>LockHolder</c>) so a real EH clause is present, and
/// assert the structured identity / clauses / GC encoding rather than scraping lines.
/// </summary>
public sealed class CodeInfoTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    // !ehinfo's clause data comes from per-method debug info the DAC only sees in a Full dump on net8-net10
    // (present in Heap dumps from net11 on); capture Full there. GcInfo_ReportsEncoding reads GC info that
    // survives the reduced Heap dump, so it stays on the default Heap Matrix.
    public static TheoryData<TestConfig> FullDumpMatrix => TestMatrices.FullDumpOnCoreVersions([TargetCatalog.Scenarios], CoreVersion.Net8 | CoreVersion.Net9 | CoreVersion.Net10);

    [SosTheory]
    [MemberData(nameof(FullDumpMatrix))]
    public async Task EhInfo_ReportsClauses(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // A method with no try/catch has native code but no EH clauses.
        EEMatch atHeap = Method(target, config.Flavor, "AtHeap");
        EhInfoResult simple = target.EhInfo(atHeap.MethodDesc!.Value);
        Assert.Equal("SosHarnessScenarios.AtHeap()", simple.MethodName);
        Assert.Empty(simple.Clauses);

        // LockHolder's lock => try/finally, so ehinfo reports a FINALLY clause. (Desktop .NET Framework's
        // JIT additionally emits a "cloned finally" with an empty clause range, so assert on the real one
        // rather than an exact count.)
        EEMatch lockHolder = Method(target, config.Flavor, "LockHolder");
        EhInfoResult eh = target.EhInfo(lockHolder.MethodDesc!.Value);
        Assert.Contains("LockHolder", eh.MethodName, StringComparison.Ordinal);
        Assert.NotEmpty(eh.Clauses);
        Assert.Contains(eh.Clauses, c =>
            c.Kind.Contains("FINALLY", StringComparison.Ordinal) &&
            c.ClauseEnd > c.ClauseStart &&
            c.HandlerEnd > c.HandlerStart);

        // ehinfo accepts an IP and resolves to the same MethodDesc.
        EhInfoResult byIp = target.EhInfo(lockHolder.JittedCodeAddress!.Value);
        Assert.Equal(lockHolder.MethodDesc.Value, byIp.MethodDesc);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task GcInfo_ReportsEncoding(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);
        EEMatch atHeap = Method(target, config.Flavor, "AtHeap");

        GcInfoResult gc = target.GcInfo(atHeap.MethodDesc!.Value);
        Assert.NotEqual(0ul, gc.EntryPoint);
        Assert.NotEqual(0ul, gc.GcInfoAddress);
        Assert.True(gc.CodeSize > 0, "expected a positive code size");
        Assert.NotEmpty(gc.Transitions);
        Assert.Contains(gc.Transitions, t => t.Contains("interruptible", StringComparison.Ordinal));

        // gcinfo accepts an IP and reports the same entry point.
        GcInfoResult byIp = target.GcInfo(atHeap.JittedCodeAddress!.Value);
        Assert.Equal(gc.EntryPoint, byIp.EntryPoint);
    }

    private static EEMatch Method(Target target, Flavor flavor, string name)
    {
        string module = TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(flavor);
        return target.Name2EE($"{module}!SosHarnessScenarios.{name}").Single;
    }
}
