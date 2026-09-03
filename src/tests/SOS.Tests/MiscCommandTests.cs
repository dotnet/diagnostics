// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Trivial session/diagnostic commands that take no target state: <c>!dbgout</c> (toggles internal debug
/// output), <c>!sosflush</c> (resets SOS's cached state), and <c>!enummem</c> (the EnumMemoryRegions test
/// command). The assertion is that each is a recognised command that executes without error. (<c>!crashinfo</c>
/// is deferred — it needs a dump written by the runtime's crash reporter, which the harness doesn't produce.)
/// </summary>
public sealed class MiscCommandTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task SessionCommands_Execute(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // dbgout toggles internal debug logging and reports the new state.
        target.Sos("dbgout").AssertContains("Debug output logging");

        // sosflush resets SOS's cached state; it produces no output but must run cleanly. The cDAC
        // implements IXCLRDataProcess::Flush (dotnet/runtime Legacy/SOSDacImpl.IXCLRDataProcess.cs), so this
        // works on every host/DAC including net11 + cDAC.
        AssertRuns(target.Sos("sosflush"));

        // enummem is not surfaced by the lldb SOS plugin; return early rather
        // than skipping — sosflush above has already been verified on this config.
        if (config.Host == Host.Lldb)
        {
            return;
        }

        // enummem (EnumMemoryRegions) is E_NOTIMPL under the cDAC on the dotnet-dump host — by design, the
        // cDAC doesn't implement the memory-region enumeration contract, surfaced as "Unrecognized SOS
        // command". The native (cdb) host services enummem itself, so
        // it's only unavailable on dotnet-dump. Return early rather than skipping — sosflush above has
        // already been verified on this config. cDAC only exists on net11+, so no version check is needed.
        if (config.Dac == Dac.CDac && config.Host == Host.DotnetDump)
        {
            return;
        }

        // enummem produces no output but must be a recognised command that runs cleanly.
        AssertRuns(target.Sos("enummem"));
    }

    private static void AssertRuns(SosOutput output)
    {
        Assert.DoesNotContain("Unrecognized", output.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not extension gallery", output.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ERROR:", output.Text, StringComparison.Ordinal);
    }
}
