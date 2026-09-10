// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;

namespace SOS.TestHarness;

/// <summary>
/// An internal, read-only debug session over a specific dump (one <c>(target, stopPoint, flavor)</c>)
/// loaded into a host, with SOS ready. Because the dump is immutable, a session is safe to reuse
/// across many assertions, and is memoized by <see cref="Targets"/> and reused by every
/// <see cref="DeadTarget"/> cursor that navigates to the same point.
///
/// Host lifetime differs by backend:
/// <list type="bullet">
///   <item><b>cdb</b> runs dbgeng in a <see cref="ChildEngineClient"/> child process. Dump sessions
///   are distributed across two capacity-1 slots, bounding retained dump mappings while allowing
///   two independent sessions to execute concurrently.</item>
///   <item><b>dotnet-dump</b> children busy-wait on stdin at ~100% CPU, so keeping many alive would
///   saturate the machine. They route through a capacity-1 <see cref="HostSlot"/> (most-recently-used
///   stays open, reopened on demand).</item>
///   <item><b>lldb</b> children retain their loaded core and hosted SOS runtime. Like cdb, they are
///   distributed across two capacity-1 slots to preserve limited concurrency while bounding memory.</item>
/// </list>
/// </summary>
internal sealed class DumpSession : IPooledHost, IDisposable
{
    private readonly Host _hostKind;
    private readonly bool _pooled;
    private readonly HostSlot? _slot;
    private readonly object _gate = new(); // serializes concurrent commands on a non-pooled host
    private IDebuggerHost? _host;

    // One diagnostics collector for the life of this session (survives a pooled host being closed and
    // reopened), for the child-process hosts that support capture. Null for the cdb child host.
    private readonly HostDiagnostics? _diagnostics;

    public Host Host { get; }
    public string TargetName { get; }
    public string StopName { get; }
    public Flavor Flavor { get; }
    public string DumpPath { get; }
    public CoreVersion CoreVersion { get; }
    public Dac Dac { get; }

    /// <summary>Captured stdout/stderr and crash dumps for this session's host, or null for the cdb child
    /// host (which does not support capture). Surfaced in a failing test's replay.</summary>
    public HostDiagnostics? Diagnostics => _diagnostics;

    internal DumpSession(Host hostKind, string targetName, string stopName, Flavor flavor, string dumpPath,
                         CoreVersion coreVersion = CoreVersion.Net10, Dac dac = Dac.Legacy)
    {
        _hostKind = hostKind;
        Host = hostKind;
        TargetName = targetName;
        StopName = stopName;
        Flavor = flavor;
        DumpPath = dumpPath;
        CoreVersion = coreVersion;
        Dac = dac;

        // Bound resource-heavy dump hosts independently. Cdb uses two slots to retain the suite's
        // observed two-test concurrency without allowing one EngineHost process per memoized session.
        _slot = HostSlotFor(hostKind);
        _pooled = _slot is not null;

        // The child-process hosts (lldb, dotnet-dump) capture their stdout/stderr and crash dumps; the cdb
        // child host runs dbgeng out-of-process and is not wired for capture.
        _diagnostics = hostKind is Host.Lldb or Host.DotnetDump ? new HostDiagnostics(hostKind.ToString().ToLowerInvariant()) : null;

        if (!_pooled)
        {
            _host = HostFactory.CreateDumpHost(hostKind, flavor, dumpPath, dac, coreVersion,
                SnapshotStore.TargetExe(flavor, targetName, coreVersion), _diagnostics);
            _host.LoadSos();
        }
    }

    /// <summary>
    /// Run a SOS command against this target (host prefixing handled by the host). A shared target
    /// may be handed to several tests at once (it is memoized by host/target/stop/flavor), and the
    /// cdb backend is a single child process whose stdin/stdout pipe is not safe for concurrent
    /// callers — so non-pooled commands are serialized on a per-target gate. Pooled paths serialize
    /// themselves on their slot locks.
    /// </summary>
    public SosOutput Sos(string command) =>
        RunCommand("SOS", command, h => h.Sos(command));

    /// <summary>Run a raw debugger command against this target.</summary>
    public SosOutput Execute(string command) =>
        RunCommand("Execute", command, h => h.Execute(command));

    private SosOutput RunCommand(string kind, string command, Func<IDebuggerHost, SosOutput> action)
    {
        try
        {
            return _pooled ? _slot!.Run(this, action) : RunGuarded(action);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException)
        {
            throw new InvalidOperationException(
                $"Dump session command failed:{Environment.NewLine}" +
                $"Host={Host}{Environment.NewLine}" +
                $"Target={TargetName}{Environment.NewLine}" +
                $"Stop={StopName}{Environment.NewLine}" +
                $"Flavor={Flavor}{Environment.NewLine}" +
                $"CoreVersion={CoreVersion}{Environment.NewLine}" +
                $"Dac={Dac}{Environment.NewLine}" +
                $"DumpPath={DumpPath}{Environment.NewLine}" +
                $"CommandKind={kind}{Environment.NewLine}" +
                $"Command={command}",
                ex);
        }
    }

    private SosOutput RunGuarded(Func<IDebuggerHost, SosOutput> action)
    {
        lock (_gate)
        {
            return action(_host!);
        }
    }

    internal static HostSlot? HostSlotFor(Host hostKind) => hostKind switch
    {
        Host.Cdb => HostSlot.CdbDump.Select(),
        Host.Lldb => HostSlot.LldbDump.Select(),
        Host.DotnetDump => HostSlot.DotNetDump.Select(),
        _ => null,
    };

    // IPooledHost — used only for pooled dump-host paths.

    IDebuggerHost IPooledHost.Host => _host!;

    void IPooledHost.OpenHost()
    {
        _host = HostFactory.CreateDumpHost(_hostKind, Flavor, DumpPath, Dac, CoreVersion,
            SnapshotStore.TargetExe(Flavor, TargetName, CoreVersion), _diagnostics);
        _host.LoadSos();
    }

    void IPooledHost.CloseHost()
    {
        _host?.Dispose();
        _host = null;
    }

    public void Dispose()
    {
        if (_pooled)
        {
            // The slot owns the pooled host's lifetime; closed at teardown via the slot.
            return;
        }

        _host?.Dispose();
        _host = null;
    }
}
