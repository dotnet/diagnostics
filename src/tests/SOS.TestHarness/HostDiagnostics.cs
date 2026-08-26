// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text;

namespace SOS.TestHarness;

/// <summary>
/// Captures everything a launched debugger host process (lldb, dotnet-dump) emitted — its command line
/// and the raw stdout/stderr transcript — plus any crash dump it dropped, so a failing test's replay can
/// show what the underlying process actually did before it wedged or died. The two symptoms this exists
/// to diagnose are a broken stdin pipe (the host process crashed out from under us) and a command timeout
/// (the host stopped answering); in both cases the useful evidence is the tail of stderr and a crash dump,
/// neither of which the harness previously retained.
///
/// The host process is run with the standard .NET crash-dump environment (see
/// <see cref="ConfigureCrashDumps"/>): because SOS's managed extension runs on a real .NET runtime inside
/// lldb (and dotnet-dump is itself a .NET process), a fatal fault in that runtime writes a full dump we can
/// point at with an <c>artifact:</c> line. Dumps for every host land in one shared directory and are
/// attributed back to the right host by the crashing process id (<see cref="RecordProcess"/>).
///
/// One instance is owned per <see cref="DumpSession"/> (so it survives the pooled dotnet-dump host being
/// closed and reopened) or per live lldb host. It is thread-safe: the stdout and stderr reader threads
/// append concurrently while the test thread reads snapshots.
/// </summary>
public sealed class HostDiagnostics
{
    // Keep only the tail of each stream: a crash's useful context is at the end, and an unbounded buffer
    // on a long-lived shared host would grow without limit across the many tests that reuse it.
    private const int MaxStreamChars = 128 * 1024;

    private static readonly string s_crashRoot =
        Environment.GetEnvironmentVariable("HELIX_WORKITEM_UPLOAD_ROOT") is { Length: > 0 } uploadRoot
            ? Path.Combine(uploadRoot, "failure-diagnostics", "crashdumps")
            : Path.Combine(RepoLayout.Root, "artifacts", "replays", "crashdumps");

    private readonly object _gate = new();
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly HashSet<int> _pids = new();
    private bool _stdoutTruncated;
    private bool _stderrTruncated;
    private string _commandLine = string.Empty;

    public HostDiagnostics(string name) => Name = name;

    /// <summary>Short host label, e.g. "lldb" or "dotnet-dump".</summary>
    public string Name { get; }

    /// <summary>The shared directory crash dumps are written to (created on demand).</summary>
    public static string CrashDumpDirectory => s_crashRoot;

    /// <summary>The launched command line (exe + args), captured for the replay.</summary>
    public string CommandLine
    {
        get { lock (_gate) { return _commandLine; } }
    }

    /// <summary>
    /// Set the standard .NET crash-dump environment on a host process so a fatal fault in its managed
    /// runtime (the SOS extension hosted inside lldb, or dotnet-dump itself) writes a full dump plus a
    /// crash report and a createdump log into the shared crash directory. Dumps are named with the crashing
    /// process id/time so <see cref="Artifacts"/> can attribute them to this host by the ids it records.
    /// </summary>
    public void ConfigureCrashDumps(ProcessStartInfo psi)
    {
        Directory.CreateDirectory(s_crashRoot);
        psi.Environment["DOTNET_DbgEnableMiniDump"] = "1";
        psi.Environment["DOTNET_DbgMiniDumpType"] = "4"; // Full — required for ClrMD/SOS analysis
        psi.Environment["DOTNET_DbgMiniDumpName"] = Path.Combine(s_crashRoot, "%e.%p.%t.dmp");
        psi.Environment["DOTNET_EnableCrashReport"] = "1";
        psi.Environment["DOTNET_CreateDumpDiagnostics"] = "1";
        // Send createdump's own diagnostics to a file rather than the host's stderr, so it neither floods
        // the transcript nor gets tangled with the SOS output we scrape for command framing.
        psi.Environment["DOTNET_CreateDumpLogToFile"] = Path.Combine(s_crashRoot, "createdump.%p.log");
        psi.Environment["DOTNET_DbgCreateDumpToolPath"] = ToolPaths.CreateDumpPath;
    }

    /// <summary>Record the launched process (its command line and pid) so a dump it writes can be found.</summary>
    public void RecordProcess(Process process)
    {
        lock (_gate)
        {
            _commandLine = $"{process.StartInfo.FileName} {string.Join(' ', process.StartInfo.ArgumentList)}".Trim();
            try
            {
                _pids.Add(process.Id);
            }
            catch
            {
                // Id can throw if the process already exited; the command line is still useful.
            }
        }
    }

    public void AppendStdout(string line) => Append(_stdout, line, ref _stdoutTruncated);

    public void AppendStderr(string line) => Append(_stderr, line, ref _stderrTruncated);

    private void Append(StringBuilder sb, string line, ref bool truncated)
    {
        lock (_gate)
        {
            sb.Append(line).Append('\n');
            if (sb.Length > MaxStreamChars)
            {
                sb.Remove(0, sb.Length - MaxStreamChars);
                truncated = true;
            }
        }
    }

    /// <summary>The captured stdout tail (empty if nothing was captured).</summary>
    public string StdoutTail() => Snapshot(_stdout, _stdoutTruncated);

    /// <summary>The captured stderr tail (empty if nothing was captured).</summary>
    public string StderrTail() => Snapshot(_stderr, _stderrTruncated);

    private string Snapshot(StringBuilder sb, bool truncated)
    {
        lock (_gate)
        {
            if (sb.Length == 0)
            {
                return string.Empty;
            }

            return (truncated ? "... (truncated, showing tail)\n" : string.Empty) + sb;
        }
    }

    /// <summary>
    /// Crash dumps, crash reports, and createdump logs this host produced — matched out of the shared
    /// crash directory by the process ids it recorded. Returns full paths for <c>artifact:</c> lines.
    /// </summary>
    public IReadOnlyList<string> Artifacts()
    {
        int[] pids;
        lock (_gate)
        {
            if (_pids.Count == 0)
            {
                return Array.Empty<string>();
            }

            pids = _pids.ToArray();
        }

        if (!Directory.Exists(s_crashRoot))
        {
            return Array.Empty<string>();
        }

        List<string> hits = new();
        foreach (string file in Directory.EnumerateFiles(s_crashRoot))
        {
            string name = Path.GetFileName(file);
            // Dump/report names embed the crashing pid as ".<pid>." (%e.%p.%t.dmp); the createdump log is
            // "createdump.<pid>.log". Match either form against the ids we launched.
            foreach (int pid in pids)
            {
                if (name.Contains($".{pid}.", StringComparison.Ordinal))
                {
                    hits.Add(file);
                    break;
                }
            }
        }

        hits.Sort(StringComparer.Ordinal);
        return hits;
    }
}
