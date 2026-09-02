// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;

namespace SOS.TestHarness;

/// <summary>
/// The single entry point tests use to get a debug target. Shared (dump-backed) targets are
/// memoized process-wide by <c>(host, target, stopPoint)</c> so the expensive load happens once
/// and is reused across every test that asks for the same triple. Live targets are exclusive and
/// never memoized — each call hands the caller its own advancing debuggee.
///
/// Shared hosts (notably the dotnet-dump child processes) must be torn down at the end of the run,
/// or their lingering child processes keep the test host alive. <see cref="DisposeAll"/> does this;
/// it is wired to <see cref="AppDomain.ProcessExit"/> and also exposed for an explicit assembly
/// teardown fixture.
/// </summary>
public static class Targets
{
    private static readonly ConcurrentDictionary<(Host Host, string Target, string Stop, Flavor Flavor, GcType GcType, DumpKind DumpKind, CoreVersion CoreVersion, Dac Dac), Lazy<DumpSession>> s_sessions = new();
    private static readonly ConcurrentBag<DumpSession> s_created = new();

    static Targets()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeAll();
    }

    /// <summary>
    /// Get a debug target for a matrix row (<see cref="TestConfig"/>). With
    /// <see cref="TestConfig.Liveness"/> = <see cref="Liveness.Dump"/> you get a dump-backed
    /// <see cref="DeadTarget"/> (navigate to cached dumps in any order); with <see cref="Liveness.Live"/>
    /// you get a <see cref="LiveTarget"/> launched and parked at the debugger's initial breakpoint.
    /// Awaiting is the "gate" — for live it completes once the process is launched and SOS is ready. The
    /// row must carry a single Host/Flavor/Liveness/GcType/DumpKind value (the per-case value a theory
    /// receives), not a combined <c>AllValid</c> selector.
    /// </summary>
    public static Task<Target> GetTargetAsync(TestConfig config)
    {
        bool live = config.Liveness switch
        {
            Liveness.Live => true,
            Liveness.Dump => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(config), config.Liveness, "Expected exactly Liveness.Live or Liveness.Dump."),
        };

        TargetDefinition definition = TargetCatalog.Get(config.Target);

        // Begin capturing this test's replay timeline (host/flavor/liveness + every command/dump).
        ReplayContext.Start(config, live);

        if (live)
        {
            return Task.Run<Target>(() => {
                string exe = SnapshotStore.TargetExe(config.Flavor, config.Target, config.CoreVersion);
                return new LiveTarget(config.Host, definition, config.Flavor, exe, config.CoreVersion, config.Dac);
            });
        }

        // Dead targets are cheap cursors; the heavy work (capture + load) happens on first GoTo,
        // memoized per point so parallel tests navigating to the same point share one dump host.
        return Task.FromResult<Target>(new DeadTarget(config));
    }

    /// <summary>
    /// Resolve (memoized, process-wide) the read-only dump session for one point — produced and SOS
    /// loaded on first use, then reused by every <see cref="DeadTarget"/> that navigates here.
    /// </summary>
    internal static DumpSession ResolveSession(TestConfig config, string stop)
    {
        return s_sessions
            .GetOrAdd((config.Host, config.Target, stop, config.Flavor, config.GcType, config.DumpKind, config.CoreVersion, config.Dac),
                key => new Lazy<DumpSession>(() => CreateSession(key)))
            .Value;
    }

    private static DumpSession CreateSession((Host Host, string Target, string Stop, Flavor Flavor, GcType GcType, DumpKind DumpKind, CoreVersion CoreVersion, Dac Dac) key)
    {
        string dump = SnapshotStore.GetDump(key.Flavor, key.Target, key.Stop, key.GcType, key.DumpKind, key.CoreVersion);
        DumpSession session = new(key.Host, key.Target, key.Stop, key.Flavor, dump, key.CoreVersion, key.Dac);
        s_created.Add(session);
        return session;
    }

    /// <summary>Dispose every memoized dump session and close pooled debugger children.</summary>
    public static void DisposeAll()
    {
        while (s_created.TryTake(out DumpSession? session))
        {
            try
            {
                session.Dispose();
            }
            catch
            {
                // best effort teardown
            }
        }

        // Close any pooled host still open. cdb children were disposed with their sessions above.
        HostSlot.Lldb.CloseCurrent();
        HostSlot.DotNetDump.CloseCurrent();
    }
}
