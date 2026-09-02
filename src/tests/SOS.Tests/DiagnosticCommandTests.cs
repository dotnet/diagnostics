// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Diagnostic / status commands the legacy <c>.script</c> suite exercised: <c>!dumpgcdata</c>,
/// <c>!sosstatus</c>, the <c>!logopen</c>/<c>!logging</c>/<c>!logclose</c> logging controls, and
/// <c>!clrma</c> (the CLRMA managed-analysis provider that drives Watson / !analyze). These report or toggle
/// session state rather than inspect objects, so the assertions check the documented banners/state.
/// </summary>
public sealed class DiagnosticCommandTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);

    // clrma drives the native CLRMA provider, which is only surfaced by the dbgeng (cdb) and managed
    // (dotnet-dump) hosts. The lldb SOS plugin never registered it (true of the legacy suite too — clrma
    // ran only under the dotnet-dump host there), so lldb is excluded from the matrix rather than skipped.
    public static TheoryData<TestConfig> ClrmaMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.Cdb | Host.DotnetDump);
    public static TheoryData<TestConfig> ClrmaExceptionMatrix =>
        TestConfig.BuildMatrix([TargetCatalog.NestedException], Flavor.AllValid, Host.DotnetDump, dumpKind: DumpKind.All);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpGcData_ReportsGcStatistics(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("dumpgcdata").AssertContains("concurrent GCs");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task SosStatus_ReportsTargetAndRuntime(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        SosOutput status = target.Sos("sosstatus");
        status.AssertContains("Target OS:");
        Assert.Contains(".NET", status.Text, StringComparison.Ordinal);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Logging_ReportsState(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // logging reports the internal-logging state (a native SOS command, both hosts).
        target.Sos("logging").AssertContains("Logging");
    }

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task LogOpenClose_CyclesConsoleLog(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // logopen/logclose are managed extension commands (the dbgeng host uses native .logopen instead).
        string logFile = Path.Combine(Path.GetTempPath(), $"soslog-{Guid.NewGuid():N}.txt");
        try
        {
            target.Sos($"logopen {logFile}").AssertContains("logging to");
            target.Sos("logclose");
        }
        finally
        {
            File.Delete(logFile);
        }
    }

    [SosTheory]
    [MemberData(nameof(ClrmaMatrix))]
    public async Task Clrma_DrivesManagedAnalysis(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // clrma drives the CLRMA provider (used by Watson / !analyze) and prints the managed thread analysis.
        SosOutput clrma = target.Sos("clrma");
        clrma.AssertContains("Managed analysis provider");
        clrma.AssertContains("OSThreadId:");
    }

    [SosTheory]
    [MemberData(nameof(ClrmaExceptionMatrix))]
    public async Task Clrma_ReportsCurrentExceptionChain(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosOutput clrma = target.Sos("clrma");
        clrma.AssertContains("Current exception:");
        Assert.Matches(@"Exception type:\s+System\.InvalidOperationException", clrma.Text);
        Assert.Matches(@"HResult:\s+80131509", clrma.Text);
        clrma.AssertContains("InnerException:");
        Assert.Matches(@"Exception type:\s+System\.FormatException", clrma.Text);
        Assert.Matches(@"HResult:\s+80131537", clrma.Text);
    }
}
