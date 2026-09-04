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
    private DumpSession? _current;

    internal DeadTarget(TestConfig config)
        : base(config.Host, config.Target, config.Flavor)
    {
        _config = config;
    }

    public override string DumpPath => Current.DumpPath;

    protected override void GoToStopPointCore(string stopName)
    {
        StopPoint stop = TargetCatalog.Get(TargetName).Stop(stopName);
        _current = Targets.ResolveSession(_config, stop.Name);
    }

    protected override void GoToCrashCore()
    {
        StopPoint crash = TargetCatalog.Get(TargetName).StopPoints.Single(s => s.Kind == StopKind.Crash);
        _current = Targets.ResolveSession(_config, crash.Name);
    }

    protected override SosOutput SosCore(string command) => Current.Sos(command);

    protected override SosOutput ExecuteCore(string command) => Current.Execute(command);

    internal override HostDiagnostics? CurrentDiagnostics => _current?.Diagnostics;

    private DumpSession Current =>
        _current ?? throw new InvalidOperationException(
            "Target is not at a point yet; call GoToStopPoint(...) or GoToCrash() before Sos.");

    // Sessions are owned and disposed by the Targets registry; the cursor owns nothing.
}
