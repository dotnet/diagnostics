// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace SOS.TestHarness;

/// <summary>The kind of a captured <see cref="ReplayStep"/>.</summary>
public enum ReplayStepKind
{
    /// <summary>A navigation (<c>GoToStopPoint</c>/<c>GoToCrash</c>/<c>RunToBreakpoint</c>).</summary>
    Navigate,

    /// <summary>A SOS command run via <see cref="Target.Sos"/>.</summary>
    Sos,

    /// <summary>A raw debugger command run via <see cref="Target.Execute"/>.</summary>
    Execute,
}

/// <summary>One recorded action against a <see cref="Target"/>: a navigation or a command, plus the
/// dump file it ran against (null for live targets, which have no dump).</summary>
public sealed class ReplayStep
{
    public ReplayStep(ReplayStepKind kind, string text, string? dumpPath)
    {
        Kind = kind;
        Text = text;
        DumpPath = dumpPath;
    }

    public ReplayStepKind Kind { get; }
    public string Text { get; }
    public string? DumpPath { get; }
}

/// <summary>
/// A running, per-test record of everything a <see cref="Target"/> did — the host/flavor/liveness it
/// was, and the ordered list of navigations and SOS/debugger commands (each tagged with the dump it
/// ran against). It is captured unconditionally and cheaply during the <c>using Target</c> window; a
/// failing test's after-hook reads it back to emit a replay artifact. Tests never touch this
/// directly — capture is automatic.
///
/// Captures are keyed by the running test's <c>UniqueID</c> in a private table (NOT in xUnit's
/// <c>KeyValueStorage</c>, which is shared across the context hierarchy and would let parallel tests
/// clobber one another). The after-hook removes the entry, so the table never grows past the set of
/// in-flight tests.
/// </summary>
public sealed class ReplayContext
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ReplayContext> s_byTest = new();

    private readonly List<ReplayStep> _steps = new();
    private readonly HashSet<HostDiagnostics> _hosts = new();

    private ReplayContext(TestConfig config, bool live)
    {
        TargetName = config.Target;
        Host = config.Host;
        Flavor = config.Flavor;
        Config = config.ToString();
        Live = live;
    }

    public string TargetName { get; }
    public Host Host { get; }
    public Flavor Flavor { get; }
    public string Config { get; }
    public bool Live { get; }
    public IReadOnlyList<ReplayStep> Steps => _steps;

    /// <summary>The host diagnostics (captured stdout/stderr and crash dumps) of every host this test
    /// touched, deduplicated. A failing test's replay renders these.</summary>
    public IReadOnlyCollection<HostDiagnostics> Hosts
    {
        get { lock (_hosts) { return _hosts.ToArray(); } }
    }

    internal void Add(ReplayStepKind kind, string text, string? dumpPath) =>
        _steps.Add(new ReplayStep(kind, text, dumpPath));

    /// <summary>Note that this test used a host, so its captured diagnostics can be surfaced on failure.
    /// No-ops for a null host (e.g. the cdb child host, which is not wired for capture).</summary>
    internal void AttachHost(HostDiagnostics? host)
    {
        if (host is null)
        {
            return;
        }

        lock (_hosts)
        {
            _hosts.Add(host);
        }
    }

    /// <summary>
    /// Begin capturing for the current test, replacing any prior capture for it. Keyed by the test's
    /// <c>UniqueID</c>, so it is isolated per test even under cross-class parallelism. No-ops (returns
    /// null) when not running inside a test.
    /// </summary>
    public static ReplayContext? Start(TestConfig config, bool live)
    {
        string? id = TestContext.Current.Test?.UniqueID;
        if (id is null)
        {
            return null;
        }

        ReplayContext replay = new(config, live);
        s_byTest[id] = replay;
        return replay;
    }

    /// <summary>The capture for the current test, or null if none was started.</summary>
    public static ReplayContext? Current
    {
        get
        {
            string? id = TestContext.Current.Test?.UniqueID;
            return id is not null && s_byTest.TryGetValue(id, out ReplayContext? replay) ? replay : null;
        }
    }

    /// <summary>Fetch and remove the capture for a test (the after-hook calls this for every test, so
    /// the table is bounded by the in-flight test set).</summary>
    public static ReplayContext? Take(string testUniqueId) =>
        s_byTest.TryRemove(testUniqueId, out ReplayContext? replay) ? replay : null;
}
