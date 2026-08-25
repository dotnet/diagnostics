// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace SOS.TestHarness;

/// <summary>
/// Bridges the ambient xunit/MTP test cancellation into the harness's blocking debugger-host waits.
///
/// <para>The debugger-host REPLs (cdb/dotnet-dump/lldb) drive a child process and block on its output with
/// a generous per-command timeout (up to ~2 minutes, since a live <c>process continue</c> can legitimately
/// run that long under a saturated matrix). Those waits used to ignore cancellation, so when the user hit
/// Ctrl+C the runner printed "Canceling the test session..." and then appeared to hang until the in-flight
/// command's timeout elapsed (or it happened to complete). Observing this token in the blocking waits lets a
/// canceled run unwind promptly — the wait throws <see cref="OperationCanceledException"/>, the test's
/// <c>using</c> target/host disposes (which tears the child process down), and the session ends.</para>
/// </summary>
internal static class HarnessCancellation
{
    /// <summary>
    /// The ambient test cancellation token, signaled by the runner on Ctrl+C / session cancellation /
    /// global timeout. Returns <see cref="CancellationToken.None"/> when there is no active test context
    /// (e.g. one-time fixture teardown), so callers can use it unconditionally.
    /// </summary>
    public static CancellationToken Token => TestContext.Current?.CancellationToken ?? CancellationToken.None;
}
