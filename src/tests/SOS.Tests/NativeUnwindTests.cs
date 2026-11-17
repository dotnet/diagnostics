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
    // Only the Core flavor has the separate host libraries asserted below; single-file statically links them into the app.
    // cdb is excluded: dbgeng cannot unwind JIT frames in these dumps without runtime help.
    public static TheoryData<TestConfig> Matrix =>
        TestConfig.BuildMatrix([TargetCatalog.SimpleThrow], Flavor.Core, Host.Lldb);

    /// <summary>
    /// The crashing thread must unwind through the runtime, JIT code, and the host back to hostpolicy.
    /// On Linux ARM64 with pointer authentication, saved return addresses are PAC-signed; LLDB can strip them
    /// only when the core records the PAC masks in an <c>NT_ARM_PAC_MASK</c> note, as kernel-generated cores do.
    /// </summary>
    [UnixTheory]
    [MemberData(nameof(Matrix))]
    public async Task CrashThread_UnwindsToHost(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToCrash();

        SosOutput backtrace = target.Execute("thread backtrace");
        backtrace.AssertContains("libcoreclr.");
        backtrace.AssertContains("libhostpolicy.");
    }
}
