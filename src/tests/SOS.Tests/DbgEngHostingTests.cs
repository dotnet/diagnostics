// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class DbgEngHostingTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix(
        [TargetCatalog.Scenarios], host: Host.Cdb, liveness: Liveness.AllValid, dumpKind: DumpKind.All | DumpKind.Full);

    public static TheoryData<TestConfig> ModulePathMatrix => TestConfig.BuildMatrix(
        [TargetCatalog.Scenarios], Flavor.Core, Host.Cdb, coreVersion: CoreVersion.All, dac: Dac.Legacy);

    [WindowsTheory]
    [MemberData(nameof(Matrix))]
    public async Task Sos_ReusesCoreClrHost(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("sethostruntime").AssertContains("Using .NET Core runtime to host the managed SOS code");
        target.Sos("sethostruntime -none").AssertContains("Using .NET Core runtime to host the managed SOS code");
        target.ClrModules().SingleByName(TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(config.Flavor));
    }

    [WindowsTheory]
    [MemberData(nameof(ModulePathMatrix))]
    public async Task EngineLoadsDebuggerModulesFromSelectedLayout(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        Dictionary<string, string> modules = target.Execute(EngineProtocol.LoadedModules).Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        Assert.Equal(Path.Combine(ToolPaths.DbgEngDirectory, "dbgeng.dll"), modules["dbgeng.dll"], ignoreCase: true);
        Assert.Equal(ToolPaths.SosPath, modules["sos.dll"], ignoreCase: true);
    }
}
