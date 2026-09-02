// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SOS.TestHarness;
using Xunit;
using Xunit.v3;

// Apply to the whole assembly: every test is wrapped, with zero per-test code. The hook only does
// work when a test FAILS and actually drove a Target, so passing tests pay only a dictionary lookup.
[assembly: SOS.Tests.SosReplay]

namespace SOS.Tests;

/// <summary>
/// Assembly-wide after-test hook: when a test fails, write a "replay" artifact capturing how to
/// reproduce it by hand — the host/flavor/liveness, the dump file(s), and the ordered SOS/debugger
/// commands the test ran (captured automatically in the <c>using Target</c> window). The artifact is
/// dropped under a per-run directory in <c>artifacts/TestResults/SOS.Tests/</c>, where CI artifact
/// collection can preserve it. Tests need no changes: capture is automatic and failure detection
/// rides on <see cref="ITestContext.TestState"/>, which is populated by the time
/// <see cref="After"/> runs.
/// </summary>
public sealed class SosReplayAttribute : BeforeAfterTestAttribute
{
    private static readonly string s_runDirectory = Path.Combine(
        RepoLayout.Root,
        "artifacts",
        "TestResults",
        "SOS.Tests",
        $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Environment.ProcessId}");

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        // Always take (and remove) this test's capture, pass or fail, so the table stays bounded.
        ReplayContext? replay = ReplayContext.Take(test.UniqueID);

        ITestContext ctx = TestContext.Current;
        if (ctx.TestState?.Result != TestResult.Failed)
        {
            return;
        }

        if (replay is null)
        {
            return; // the test never created a Target — nothing to replay
        }

        string content = ReplayRenderer.Render(test.TestDisplayName, ctx.TestState, replay);

        try
        {
            Directory.CreateDirectory(s_runDirectory);
            string path = Path.Combine(s_runDirectory, ReplayRenderer.FileName(test.TestDisplayName));
            File.WriteAllText(path, content);
            ctx.TestOutputHelper?.WriteLine($"SOS replay written to: {path}");
        }
        catch (System.Exception e)
        {
            // Never let replay capture turn a test failure into an unrelated error.
            ctx.TestOutputHelper?.WriteLine($"SOS replay file could not be written: {e.Message}");
        }
    }
}

/// <summary>Renders a <see cref="ReplayContext"/> (plus the xUnit failure state) into a human- and
/// copy-paste-friendly replay script.</summary>
internal static class ReplayRenderer
{
    public static string Render(string testName, TestResultState state, ReplayContext replay)
    {
        StringBuilder sb = new();
        sb.AppendLine("# ───────────────────────────────────────────────────────────────────────────");
        sb.AppendLine("# SOS replay");
        sb.AppendLine($"# test:    {testName}");
        sb.AppendLine($"# result:  {state.Result}");
        sb.AppendLine($"# config:  {replay.Config}");
        sb.AppendLine($"# crash dump dir: {HostDiagnostics.CrashDumpDirectory}");
        sb.AppendLine($"# lldb trace:     {Environment.GetEnvironmentVariable("SOSHARNESS_LLDB_TRACE") ?? "<unset>"}");
        sb.AppendLine("# ───────────────────────────────────────────────────────────────────────────");

        AppendFailure(sb, state);
        AppendArtifacts(sb, replay);
        AppendTimeline(sb, replay);
        AppendReplay(sb, replay);
        AppendHostOutput(sb, replay);

        return sb.ToString();
    }

    private static void AppendFailure(StringBuilder sb, TestResultState state)
    {
        sb.AppendLine("#");
        sb.AppendLine("# --- failure ---------------------------------------------------------------");
        string?[] types = state.ExceptionTypes ?? [];
        string?[] messages = state.ExceptionMessages ?? [];
        for (int i = 0; i < types.Length; i++)
        {
            string message = i < messages.Length ? OneLine(messages[i] ?? string.Empty) : string.Empty;
            sb.AppendLine($"# {types[i]}: {message}");
        }

        string? stack = (state.ExceptionStackTraces ?? System.Array.Empty<string?>()).FirstOrDefault();
        foreach (string line in (stack ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                sb.AppendLine($"#   {line.TrimEnd()}");
            }
        }
    }

    /// <summary>
    /// List any crash dumps the debugger host(s) dropped as plain <c>artifact:</c> lines (deliberately not
    /// comment-prefixed, so they're easy to grep and hand straight to a debugger). A "Pipe is broken" or a
    /// command timeout usually means the host process crashed out from under us; if its hosted .NET runtime
    /// wrote a dump, this is where to find it.
    /// </summary>
    private static void AppendArtifacts(StringBuilder sb, ReplayContext replay)
    {
        List<string> artifacts = replay.Hosts.SelectMany(h => h.Artifacts()).Distinct().OrderBy(p => p, System.StringComparer.Ordinal).ToList();
        if (artifacts.Count == 0)
        {
            return;
        }

        sb.AppendLine("#");
        sb.AppendLine("# --- artifacts (crash dumps written by the host process) -------------------");
        foreach (string artifact in artifacts)
        {
            sb.AppendLine($"artifact: {artifact}");
        }
    }

    /// <summary>
    /// Emit the captured stdout and stderr of each debugger host the test drove. stderr in particular was
    /// previously discarded, yet it carries the crash diagnostics, python errors, and unhandled-exception
    /// traces that explain a broken pipe or a wedge.
    /// </summary>
    private static void AppendHostOutput(StringBuilder sb, ReplayContext replay)
    {
        foreach (HostDiagnostics host in replay.Hosts)
        {
            string stderr = host.StderrTail();
            string stdout = host.StdoutTail();
            if (stderr.Length == 0 && stdout.Length == 0 && host.CommandLine.Length == 0)
            {
                continue;
            }

            sb.AppendLine("#");
            sb.AppendLine($"# --- host process: {host.Name} ------------------------------------------------");
            if (host.CommandLine.Length > 0)
            {
                sb.AppendLine($"# launched: {host.CommandLine}");
            }

            AppendStream(sb, "stderr", stderr);
            AppendStream(sb, "stdout", stdout);
        }
    }

    private static void AppendStream(StringBuilder sb, string label, string content)
    {
        sb.AppendLine($"# --- {label} ---");
        if (content.Length == 0)
        {
            sb.AppendLine("# (empty)");
            return;
        }

        foreach (string line in content.Replace("\r", string.Empty).Split('\n'))
        {
            if (line.Length > 0)
            {
                sb.AppendLine($"# {line}");
            }
        }
    }

    private static void AppendTimeline(StringBuilder sb, ReplayContext replay)
    {
        sb.AppendLine("#");
        sb.AppendLine("# --- timeline (everything the target did, in order) ------------------------");
        foreach (ReplayStep step in replay.Steps)
        {
            sb.AppendLine($"# {step.Kind,-8} {step.Text}");
        }
    }

    private static void AppendReplay(StringBuilder sb, ReplayContext replay)
    {
        sb.AppendLine("#");
        sb.AppendLine("# --- replay ----------------------------------------------------------------");
        if (replay.Live)
        {
            sb.AppendLine("# Live target: not reproducible from a dump. Launch the debuggee under the");
            sb.AppendLine("# host and re-issue the navigations/commands from the timeline above.");
            return;
        }

        // Group consecutive commands by the dump they ran against; dead-target navigation switches
        // dumps, so each group is one `dotnet-dump analyze <dump>` session.
        string? currentDump = null;
        bool any = false;
        foreach (ReplayStep step in replay.Steps)
        {
            if (step.Kind == ReplayStepKind.Navigate)
            {
                continue;
            }

            any = true;
            if (step.DumpPath != currentDump)
            {
                currentDump = step.DumpPath;
                sb.AppendLine();
                sb.AppendLine($"dotnet-dump analyze \"{currentDump}\"");
            }

            sb.AppendLine($"> {step.Text}");
        }

        if (!any)
        {
            sb.AppendLine("# (no commands were run before the failure)");
        }
    }

    /// <summary>A filesystem-safe file name derived from the test display name.</summary>
    public static string FileName(string testName)
    {
        char[] chars = testName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        string safe = new(chars);
        if (safe.Length > 150)
        {
            safe = safe[..150];
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testName)))[..12];
        return $"{safe}_{hash}.replay.txt";
    }

    private static string OneLine(string? s) => (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
}
