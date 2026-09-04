// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// A live, advancing target owned exclusively by one test. The debuggee is launched under a child
/// <see cref="ChildEngineClient"/> (its own EngineHost process) and parked at the debugger's initial
/// breakpoint - before CoreCLR loads - with SOS loaded. <see cref="Sos"/> already works there (e.g.
/// <c>bpmd</c>, which sets a pending managed breakpoint), so it does <em>not</em> throw before the
/// first navigation, unlike a <see cref="DeadTarget"/>.
///
/// Navigation only moves forward and tracks where we are stopped: re-asking for the current point is
/// a no-op; asking for a later one runs there (skipping intervening stop points); if the process
/// crashes or exits before reaching it, an <see cref="InvalidOperationException"/> is thrown.
/// <see cref="RunToBpmd"/> is the raw form for breaking on an arbitrary method. Dispose (use
/// <c>using</c>) to shut the child down.
/// </summary>
public sealed class LiveTarget : Target
{
    // Sentinel for "stopped at the crash" - not a real stop-point name (can't collide).
    private const string CrashMarker = "\0crash";

    // Live debugging does not parallelize like dump replay: every live session spawns its own debugger
    // (lldb) ptrace-attached to a running debuggee. Letting xunit launch one per core (≈20 here)
    // overwhelms the box - lldb sessions begin to busy-spin and never return, stacking command timeouts
    // into an apparent hang. Bound the number of concurrent live sessions so a few slow ones can't
    // saturate every core. Override with SOSHARNESS_MAX_LIVE; default leaves generous headroom below the
    // core count.
    private static readonly SemaphoreSlim s_liveGate = new(ComputeMaxConcurrentLive());

    private readonly TargetDefinition _definition;
    private ILiveDebuggerHost? _host;
    private bool _gateHeld;
    private string? _at; // current stop name, CrashMarker, or null (still at the initial break)
    private bool _disposed;

    internal LiveTarget(Host hostKind, TargetDefinition definition, Flavor flavor, string exePath,
                        CoreVersion coreVersion = CoreVersion.Net10, Dac dac = Dac.Legacy)
        : base(hostKind, definition.Name, flavor)
    {
        _definition = definition;

        // Hold the live-session slot for the entire lifetime of this target (launch -> commands ->
        // dispose), not just creation, so the cap actually bounds concurrent live debuggers. Observe the
        // ambient test cancellation so a Ctrl+C while queued behind the gate unwinds promptly instead of
        // blocking until a slot frees.
        s_liveGate.Wait(HarnessCancellation.Token);
        _gateHeld = true;
        try
        {
            _host = HostFactory.CreateLiveHost(hostKind, flavor, exePath, coreVersion, dac);
        }
        catch
        {
            s_liveGate.Release();
            _gateHeld = false;
            throw;
        }
    }

    private static int ComputeMaxConcurrentLive()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("SOSHARNESS_MAX_LIVE"), out int configured) && configured > 0)
        {
            return configured;
        }

        return Math.Clamp(Environment.ProcessorCount / 4, 2, 6);
    }

    protected override void GoToStopPointCore(string stopName)
    {
        StopPoint stop = _definition.Stop(stopName);
        if (stop.Method is null)
        {
            throw new InvalidOperationException(
                $"Stop point '{stopName}' has no method to break on (kind {stop.Kind}); use GoToCrash().");
        }

        if (_at == stop.Name)
        {
            return; // already here
        }

        SkipIfLiveBpmdUnsupported();

        // Runs forward to the marker; throws if the process exits/crashes before reaching it. The
        // managed module for bpmd is flavor-specific (desktop's is the EXE, .NET Core's the DLL).
        Engine.RunToBpmd(_definition.ModuleFor(Flavor), stop.Method);
        _at = stop.Name;
    }

    protected override void GoToCrashCore()
    {
        if (_at == CrashMarker)
        {
            return; // already at the crash; repeatable no-op
        }

        Engine.RunToCrash(); // throws if the process exits without crashing
        _at = CrashMarker;
    }

    /// <summary>
    /// Resume the live process until it next hits a breakpoint. Unlike <see cref="GoToStopPoint"/>
    /// this sets and clears nothing — the caller arms the breakpoint (e.g.
    /// <c>Sos("bpmd Module Method")</c>) and this just runs to it. Throws if the process exits first.
    /// </summary>
    public void RunToBreakpoint()
    {
        SkipIfLiveBpmdUnsupported();

        Engine.RunToBreakpoint();
        _at = null; // arbitrary, caller-managed location — not a named point
        ReplayContext.Current?.Add(ReplayStepKind.Navigate, "RunToBreakpoint()", null);
    }

    /// <summary>
    /// Live <c>bpmd</c> cannot bind in a self-contained single-file image under the lldb host: bpmd arms a
    /// JIT/prestub notification breakpoint on a CoreCLR routine, but in a self-contained single-file
    /// publish the runtime is statically linked into the (symbol-stripped) app image, so lldb has no
    /// symbol to place that breakpoint on and the debuggee simply runs past every managed stop point. The
    /// .NET Core flavor keeps CoreCLR as a distinct <c>libcoreclr.so</c> module, so the same notification
    /// breakpoint resolves there. This applies uniformly to every live, single-file, lldb test that
    /// navigates via a managed stop point, so it is enforced here rather than per test.
    /// </summary>
    private void SkipIfLiveBpmdUnsupported()
    {
        if (Host == Host.Lldb && Flavor == Flavor.SingleFile)
        {
            HarnessSkipException.Now(
                "live bpmd cannot bind in a single-file image under lldb (CoreCLR is statically linked and " +
                "symbol-stripped)");
        }
    }

    protected override SosOutput SosCore(string command) => Engine.Sos(command);

    protected override SosOutput ExecuteCore(string command) => Engine.Execute(command);

    internal override HostDiagnostics? CurrentDiagnostics => (_host as IDiagnosticHost)?.Diagnostics;

    private ILiveDebuggerHost Engine =>
        _host ?? throw new ObjectDisposedException(nameof(LiveTarget));

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host?.Dispose();
        _host = null;

        if (_gateHeld)
        {
            _gateHeld = false;
            s_liveGate.Release();
        }
    }
}
