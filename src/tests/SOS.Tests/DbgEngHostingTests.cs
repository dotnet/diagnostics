// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class DbgEngHostingTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix(
        [TargetCatalog.Scenarios], host: Host.Cdb, liveness: Liveness.AllValid, dumpKind: DumpKind.All | DumpKind.Full);

    [WindowsTheory]
    [MemberData(nameof(Matrix))]
    public async Task Sos_ReusesCoreClrHost(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("sethostruntime").AssertContains("Using .NET Core runtime to host the managed SOS code");
        target.Sos("sethostruntime -none").AssertContains("Runtime hosting already initialized");
        target.ClrModules().SingleByName(TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(config.Flavor));
    }
}
