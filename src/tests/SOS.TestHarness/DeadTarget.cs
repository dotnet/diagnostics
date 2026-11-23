// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// A dump-backed target. It is a lightweight <em>cursor</em>: each <see cref="GoToStopPoint"/> /
/// <see cref="GoToCrash"/> resolves the (process-wide memoized, read-only) <see cref="DumpSession"/>
/// for that point and makes it current, so many tests navigating to the same point share one loaded
/// dump host. Because nothing advances, points can be visited in any order and revisited.
/// <see cref="Sos"/> throws until the first navigation, since there is no dump loaded yet.
/// </summary>
public sealed class DeadTarget : Target
{
    private readonly TestConfig _config;
    private readonly DumpSession? _boundSession;
    private DumpSession? _current;

    internal DeadTarget(TestConfig config, DumpSession? boundSession = null)
        : base(config.Host, config.Target, config.Flavor)
    {
        _config = config;
        _boundSession = boundSession;
    }

    public override string DumpPath => Current.DumpPath;
    public override IReadOnlyList<string> HostStartupDiagnostics => Current.StartupDiagnostics;

    protected override void GoToStopPointCore(string stopName)
    {
        StopPoint stop = TargetCatalog.Get(TargetName).Stop(stopName);
        _current = ResolveSession(stop.Name);
    }

    protected override void GoToCrashCore()
    {
        StopPoint crash = TargetCatalog.Get(TargetName).StopPoints.Single(s => s.Kind == StopKind.Crash);
        _current = ResolveSession(crash.Name);
    }

    private DumpSession ResolveSession(string stop)
    {
        if (_boundSession is null)
        {
            return Targets.ResolveSession(_config, stop);
        }

        if (_boundSession.StopName != stop)
        {
            throw new InvalidOperationException("A collection-owned target cannot navigate to a different stop.");
        }

        return _boundSession;
    }

    protected override SosOutput SosCore(string command) => Current.Sos(command);

    protected override SosOutput ExecuteCore(string command) => Current.Execute(command);

    public override IDisposable AcquireDebuggerSession() => Current.AcquireDebuggerSession();

    internal override HostDiagnostics? CurrentDiagnostics => _current?.Diagnostics;

    private DumpSession Current =>
        _current ?? throw new InvalidOperationException(
            "Target is not at a point yet; call GoToStopPoint(...) or GoToCrash() before Sos.");

    // The registry or collection fixture owns the session; the cursor owns nothing.
}
