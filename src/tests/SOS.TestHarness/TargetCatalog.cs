// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>How a named stop point is realized when producing a dump.</summary>
public enum StopKind
{
    /// <summary>Mid-run self-snapshot (the debuggee dumps itself and continues).</summary>
    Snapshot,

    /// <summary>The final unhandled-exception crash dump produced by the runtime.</summary>
    Crash,
}

/// <summary>
/// A named location in a target. The same definition drives both worlds: a dump for the
/// snapshot/shared path, and a <c>bpmd</c> breakpoint on <see cref="Method"/> for the live path.
/// </summary>
/// <param name="Name">Stable name used to key the dump and to ask a live target to stop here.</param>
/// <param name="Kind">How the dump for this stop is produced.</param>
/// <param name="Method">Fully-qualified marker method for live <c>bpmd</c> (null for crash stops).</param>
public sealed record StopPoint(string Name, StopKind Kind, string? Method);

/// <summary>A standalone test target (its own program) and its stop points.</summary>
/// <param name="Name">Target name used by tests, e.g. "divzero".</param>
/// <param name="Project">
/// The target's project/assembly name under the repo's <c>SOS.UnitTests/Debuggees</c> tree, e.g.
/// "DivZero". This is the folder, the csproj, and the produced <c>&lt;Project&gt;.exe</c> /
/// <c>&lt;Project&gt;.dll</c>.
/// </param>
/// <param name="StopPoints">Ordered named stop points.</param>
/// <param name="Flavors">
/// The flavors this target supports. Defaults to all; e.g. DynamicMethod uses a .NET-Core-only API
/// (<c>DynamicMethod.CreateDelegate&lt;T&gt;</c>) so it can't build for desktop .NET Framework.
/// </param>
public sealed record TargetDefinition(string Name, string Project, IReadOnlyList<StopPoint> StopPoints, Flavor Flavors = Flavor.AllValid)
{
    /// <summary>Managed module name for <c>bpmd</c> on .NET Core (e.g. "SosHarnessScenarios.dll").</summary>
    public string Module => Project + ".dll";

    /// <summary>
    /// Managed module name for <c>bpmd</c> in a given flavor. Desktop .NET Framework's managed
    /// module is the EXE itself (e.g. "SosHarnessScenarios.exe"); .NET Core's is the DLL.
    /// </summary>
    public string ModuleFor(Flavor flavor) => flavor == Flavor.Framework ? Project + ".exe" : Project + ".dll";

    public StopPoint Stop(string name) =>
        StopPoints.FirstOrDefault(s => s.Name == name)
        ?? throw new ArgumentException($"Target '{Name}' has no stop point '{name}'. Known: {string.Join(", ", StopPoints.Select(s => s.Name))}");

    public string DefaultStopName => StopPoints[0].Name;
}

/// <summary>
/// The debuggee targets the SOS test harness knows about, mapped to the diagnostics repo's existing
/// <c>SOS.UnitTests/Debuggees</c> projects plus the one consolidated marker debuggee the harness adds
/// (<see cref="Scenarios"/>). Crash targets reproduce an unhandled exception / fault that the runtime
/// turns into a crash dump; the marker debuggee self-snapshots at named <c>TestHarness.Stop</c> points
/// (live tests set a <c>bpmd</c> breakpoint on the same marker method).
/// </summary>
public static class TargetCatalog
{
    // --- Repo crash debuggees (unhandled exception / fault -> crash dump). ---

    public const string NestedException = "nestedexception";
    public const string DivZero = "divzero";
    public const string AsyncMain = "asyncmain";
    public const string DynamicMethod = "dynamicmethod";
    public const string Overflow = "overflow";
    public const string LineNums = "linenums";
    public const string SimpleThrow = "simplethrow";
    public const string Reflection = "reflection";

    // --- The one consolidated marker debuggee the harness adds (Phase 4). Every snapshot/oracle/live
    //     scenario is a named stop point on this single program (see SosHarnessScenarios). ---

    public const string Scenarios = "scenarios";

    // Stop-point names on the Scenarios debuggee (kept as constants so tests don't stringly-type them).
    public const string StopThinLock = "thinlock";
    public const string StopHeap = "heap";
    public const string StopArgsLocals = "argslocals";
    public const string StopRoots = "roots";
    public const string StopGen0 = "gen0";
    public const string StopGen1 = "gen1";
    public const string StopGen2 = "gen2";
    public const string StopAllThreads = "allthreads";

    private const string ScenariosProject = "SosHarnessScenarios";

    // The Scenarios stop points, shared by the workstation and server-GC targets. Heap stays first so
    // GoToFirstStop() lands there; the rest are ordered as they occur, and dumps are keyed by name (not
    // array order), so the debuggee's call order can differ.
    private static readonly StopPoint[] s_scenarioStops =
    {
        new(StopHeap, StopKind.Snapshot, $"{ScenariosProject}.AtHeap"),
        new(StopThinLock, StopKind.Snapshot, $"{ScenariosProject}.AtThinLock"),
        new(StopArgsLocals, StopKind.Snapshot, $"{ScenariosProject}.AtArgsLocals"),
        new(StopRoots, StopKind.Snapshot, $"{ScenariosProject}.AtRoots"),
        new(StopGen0, StopKind.Snapshot, $"{ScenariosProject}.AtGen0"),
        new(StopGen1, StopKind.Snapshot, $"{ScenariosProject}.AtGen1"),
        new(StopGen2, StopKind.Snapshot, $"{ScenariosProject}.AtGen2"),
        new(StopAllThreads, StopKind.Snapshot, $"{ScenariosProject}.AtAllThreads"),
    };

    private static readonly Dictionary<string, TargetDefinition> s_targets = new[]
    {
        new TargetDefinition(
            NestedException,
            Project: "NestedExceptionTest",
            StopPoints: new[] { new StopPoint("crash", StopKind.Crash, null) }),

        new TargetDefinition(
            DivZero,
            Project: "DivZero",
            StopPoints: new[] { new StopPoint("crash", StopKind.Crash, null) }),

        new TargetDefinition(
            AsyncMain,
            Project: "AsyncMain",
            StopPoints: new[] { new StopPoint("crash", StopKind.Crash, null) }),

        new TargetDefinition(
            DynamicMethod,
            Project: "DynamicMethod",
            StopPoints: new[] { new StopPoint("crash", StopKind.Crash, null) },
            // DynamicMethod.CreateDelegate<T>() is a .NET-Core-only API, so this debuggee can't build
            // for desktop .NET Framework.
            Flavors: Flavor.Core | Flavor.SingleFile),

        new TargetDefinition(
            Overflow,
            Project: "Overflow",
            StopPoints: new[] { new StopPoint("crash", StopKind.Crash, null) }),

        new TargetDefinition(
            LineNums,
            Project: "LineNums",
            StopPoints: new[] { new StopPoint("crash", StopKind.Crash, null) }),

        new TargetDefinition(
            SimpleThrow,
            Project: "SimpleThrow",
            StopPoints: new[] { new StopPoint("crash", StopKind.Crash, null) }),

        new TargetDefinition(
            Reflection,
            Project: "ReflectionTest",
            StopPoints: new[] { new StopPoint("crash", StopKind.Crash, null) }),

        // The consolidated marker debuggee: each scenario is a NoInlining marker method that calls
        // TestHarness.Stop(name). Ordered so the heap scenario (live/dead objects) is captured before
        // any GC runs for the generation-promotion stops.
        new TargetDefinition(
            Scenarios,
            Project: ScenariosProject,
            StopPoints: s_scenarioStops),
    }.ToDictionary(t => t.Name);

    public static TargetDefinition Get(string name) =>
        s_targets.TryGetValue(name, out TargetDefinition? t)
            ? t
            : throw new ArgumentException($"Unknown target '{name}'. Known: {string.Join(", ", s_targets.Keys)}");

    /// <summary>
    /// The flavors a target supports, or <see cref="Flavor.AllValid"/> when <paramref name="name"/> is
    /// not a known target (some tests pass other tokens — e.g. a stop name — through the matrix's string
    /// dimension, which the flavor filter must tolerate).
    /// </summary>
    public static Flavor FlavorsFor(string name) =>
        s_targets.TryGetValue(name, out TargetDefinition? t) ? t.Flavors : Flavor.AllValid;

    /// <summary>
    /// Whether reaching this target's stop points requires a live <c>bpmd</c> notification breakpoint
    /// (i.e. it has a <see cref="StopKind.Snapshot"/> stop), as opposed to simply running to a crash.
    /// Snapshot navigation can't be performed live on a self-contained single-file image under the lldb
    /// host, so the matrix prunes that one row for such targets.
    /// Unknown tokens are treated as not requiring bpmd.
    /// </summary>
    public static bool NavigatesViaBpmd(string name) =>
        s_targets.TryGetValue(name, out TargetDefinition? t) && t.StopPoints.Any(s => s.Kind == StopKind.Snapshot);
}
