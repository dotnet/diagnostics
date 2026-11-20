// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Validates that the native debugger (cdb on Windows, LLDB elsewhere) can unwind the crashing thread of a createdump-generated
/// dump into the runtime. Core only: single-file links the runtime into the app, so there is no coreclr module to match.
/// </summary>
public sealed class NativeUnwindTests
{
    public static TheoryData<TestConfig> Matrix =>
        TestConfig.BuildMatrix([TargetCatalog.SimpleThrow], Flavor.Core, Host.Cdb | Host.Lldb);

    /// <summary>
    /// The crashing thread must natively unwind from the crash-dump wait through several runtime frames. On Linux ARM64 with
    /// pointer authentication, saved return addresses are PAC-signed; LLDB can strip them only when the core records the PAC
    /// masks in an <c>NT_ARM_PAC_MASK</c> note, as kernel-generated cores do. Without it the walk stops in libc.
    /// </summary>
    /// <remarks>
    /// Walking past the first JIT frame is not asserted: cdb cannot do it for .NET 8, and on macOS the createdump Mach-O core
    /// records the MAP_JIT code region as rw-, so LLDB rejects the caller pc. Frames are matched by module so that unsymbolized
    /// runtimes still count.
    /// </remarks>
    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task CrashThread_UnwindsIntoRuntime(TestConfig config)
    {
        // On musl the crashing thread sits in the hidden assembly stub __cp_end (syscall_cp.s), which has no CFI. Stripped musl
        // gives LLDB no function bounds for instruction profiling, so the walk stops at frame 0 in libc. It unwinds with musl-dbg.
        Assert.SkipWhen(RepoLayout.IsMusl, "LLDB cannot unwind out of stripped musl syscall stubs without musl-dbg.");

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToCrash();

        if (config.Host == Host.Cdb)
            target.Execute("k 200").AssertLinesContaining("coreclr", 3);
        else
            target.Execute("thread backtrace").AssertLinesContaining("libcoreclr.", 3);
    }
}
