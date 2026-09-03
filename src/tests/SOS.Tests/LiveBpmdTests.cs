// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Live debugging: the debuggee is launched, a <c>bpmd</c> breakpoint is set on an arbitrary method,
/// and the process is run to it — exercising the bpmd breakpoint mechanism itself (as opposed to the
/// snapshot tests, which reach their marker stop points through the shared stop-point system). A live
/// target is exclusive and advancing, so the test owns it (<c>using</c>) rather than sharing it.
/// </summary>
public sealed class LiveBpmdTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.DivZero], Flavor.AllValid, Host.AllValid, Liveness.Live);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task RawBpmd_BreaksOnArbitraryMethod(TestConfig config)
    {
        Assert.Equal(Liveness.Live, config.Liveness);

        using LiveTarget target = (LiveTarget)await Targets.GetTargetAsync(config);

        // Raw bpmd: set a breakpoint on an arbitrary method (one that is NOT a wired stop point), then
        // run to it. DivZero.C.F2 is reached early (Main -> F1 -> F2) and is not a marker method. The
        // managed module name is flavor-specific (desktop's is the EXE, .NET Core's the DLL) — ModuleFor
        // encapsulates that.
        string module = TargetCatalog.Get(TargetCatalog.DivZero).ModuleFor(config.Flavor);
        target.Sos($"bpmd {module} C.F2");

        target.RunToBreakpoint();
        SosOutput clrstack = target.Sos("clrstack");
        clrstack.AssertContains("C.F2");
    }
}
