// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class DbgEngHostingTests
{
    public static TheoryData<TestConfig> ModulePathMatrix => TestConfig.BuildMatrix(
        [TargetCatalog.Scenarios], Flavor.Core, Host.Cdb, coreVersion: CoreVersion.All, dac: Dac.Legacy);

    [WindowsTheory]
    [MemberData(nameof(ModulePathMatrix))]
    public async Task EngineLoadsDebuggerModulesFromSelectedLayout(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        Assert.Contains(
            target.HostStartupDiagnostics,
            diagnostic => diagnostic.Equals(
                $"Loaded dbgeng.dll: {Path.Combine(ToolPaths.DbgEngDirectory, "dbgeng.dll")}",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            target.HostStartupDiagnostics,
            diagnostic => diagnostic.Equals($"Loaded sos.dll: {ToolPaths.SosPath}", StringComparison.OrdinalIgnoreCase));
    }
}
