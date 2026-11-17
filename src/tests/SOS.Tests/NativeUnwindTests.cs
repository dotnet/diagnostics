// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Validates that LLDB can natively unwind a createdump-generated core end to end, without SOS.
/// </summary>
public sealed class NativeUnwindTests
{
    // Native unwinding does not depend on the DAC, and only the Core flavor has the host libraries
    // asserted below; single-file statically links them into the app.
    public static TheoryData<TestConfig> Matrix =>
        TestConfig.BuildMatrix([TargetCatalog.SimpleThrow], Flavor.Core, Host.Lldb, dac: Dac.Legacy);

    /// <summary>
    /// The crashing thread waits in libc for createdump, so LLDB must unwind libc, libcoreclr, JIT code,
    /// and the host back to <c>corehost_main</c>. On Linux ARM64 with pointer authentication, saved return
    /// addresses are PAC-signed; LLDB can strip them only when the core records the PAC masks in an
    /// <c>NT_ARM_PAC_MASK</c> note, as kernel-generated cores do.
    /// </summary>
    [UnixTheory]
    [MemberData(nameof(Matrix))]
    public async Task CrashThread_UnwindsToHostEntryPoint(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToCrash();

        SosOutput backtrace = target.Execute("thread backtrace");
        backtrace.AssertContains("libcoreclr.so`");
        backtrace.AssertContains("corehost_main");
    }
}
