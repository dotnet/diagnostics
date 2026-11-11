// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// The tiny line protocol between the test host and the <c>EngineHost</c> child process that hosts
/// dbgeng. The child prints <see cref="Ready"/> once the target is open, then for each command line
/// it receives it prints the captured output followed by <see cref="End"/> on its own line. The
/// special request <see cref="RunToBpmdPrefix"/> asks the child to set a managed breakpoint and run
/// to it. Markers are deliberately unlikely to appear in SOS output.
/// </summary>
internal static class EngineProtocol
{
    public const string Ready = "<<<SOSHARNESS-ENGINE-READY>>>";
    public const string End = "<<<SOSHARNESS-CMD-END-9F3A>>>";
    public const string Error = "<<<SOSHARNESS-CMD-ERROR-9F3A>>>";

    /// <summary>Request: <c>@runtobpmd &lt;module&gt; &lt;method&gt;</c>.</summary>
    public const string RunToBpmdPrefix = "@runtobpmd ";

    /// <summary>Request: <c>@runtocrash</c> — run the live process to its second-chance crash.</summary>
    public const string RunToCrash = "@runtocrash";

    /// <summary>Request: <c>@runtobreak</c> — resume the live process to the next breakpoint.</summary>
    public const string RunToBreak = "@runtobreak";
}
