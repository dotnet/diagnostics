// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using Xunit;
using Xunit.Sdk;

namespace SOS.TestHarness;

/// <summary>
/// One row of the test matrix: the full set of axes that define a single debug-target configuration a
/// theory runs against. Replaces the old positional <c>(Host, Flavor, Liveness)</c> tuple so adding axes
/// (GC type, dump kind, ...) doesn't widen every signature. A test takes a single <see cref="TestConfig"/>
/// parameter and hands it straight to <see cref="Targets.GetTargetAsync(TestConfig)"/>.
///
/// <para>Implements <see cref="IXunitSerializable"/> so each row has a stable, individually-runnable test
/// id and a legible display name (see <see cref="ToString"/>), and has value equality so
/// <see cref="BuildMatrix"/> can de-duplicate rows that collapse onto the same configuration.</para>
/// </summary>
public sealed record TestConfig : IXunitSerializable
{
    private string _target = string.Empty;
    private Host _host;
    private Flavor _flavor;
    private Liveness _liveness;
    private GcType _gcType;
    private CoreVersion _coreVersion;
    private Dac _dac;
    private DumpKind _dumpKind;

    /// <summary>The debuggee target name (e.g. <see cref="TargetCatalog.Scenarios"/>).</summary>
    public string Target { get => _target; init => _target = value; }

    /// <summary>The debugger host (cdb / dotnet-dump / lldb).</summary>
    public Host Host { get => _host; init => _host = value; }

    /// <summary>The runtime flavor (Core / SingleFile / Framework).</summary>
    public Flavor Flavor { get => _flavor; init => _flavor = value; }

    /// <summary>Live process vs. post-mortem dump.</summary>
    public Liveness Liveness { get => _liveness; init => _liveness = value; }

    /// <summary>Workstation vs. server GC.</summary>
    public GcType GcType { get => _gcType; init => _gcType = value; }

    /// <summary>The .NET Core runtime version the target is built and dumped against (a single flag).</summary>
    public CoreVersion CoreVersion { get => _coreVersion; init => _coreVersion = value; }

    /// <summary>Which DAC SOS debugs with (Legacy / CDac). cDAC is only valid on .NET 11+ (see <see cref="IsValid"/>).</summary>
    public Dac Dac { get => _dac; init => _dac = value; }

    /// <summary>The dump kind (Heap / Mini). Always <see cref="DumpKind.Heap"/> for live targets (no dump).</summary>
    public DumpKind DumpKind { get => _dumpKind; init => _dumpKind = value; }

    /// <summary>Parameterless ctor required by <see cref="IXunitSerializable"/>; do not use directly.</summary>
    public TestConfig()
    {
    }

    public TestConfig(string target, Host host, Flavor flavor, Liveness liveness,
                      GcType gcType = GcType.Workstation, DumpKind dumpKind = DumpKind.Heap,
                      CoreVersion coreVersion = CoreVersion.Net10, Dac dac = Dac.Legacy)
    {
        Target = target;
        Host = host;
        Flavor = flavor;
        Liveness = liveness;
        GcType = gcType;
        DumpKind = dumpKind;
        CoreVersion = coreVersion;
        Dac = dac;
    }

    /// <summary>True for a live process target; false for a post-mortem dump.</summary>
    public bool IsLive => Liveness == Liveness.Live;

    /// <summary>
    /// Generate the cross-product of the requested axes as a single-column theory source, filtered to the
    /// valid configurations for the current platform (see <see cref="IsValid"/>).
    ///
    /// <para>Axis defaults are deliberate: <paramref name="host"/>/<paramref name="flavor"/> default to
    /// <c>AllValid</c> (full coverage), but <paramref name="liveness"/> defaults to
    /// <see cref="Liveness.Dump"/>, <paramref name="gcType"/> to <see cref="GcType.Workstation"/>, and
    /// <paramref name="dumpKind"/> to <see cref="DumpKind.Heap"/>. Live debugging is slow (a debugger
    /// ptrace-attached to a running process, one session per core) and almost every command behaves
    /// identically against a dump, so live coverage is <em>opt-in</em>: a test that uniquely benefits from a
    /// live process (e.g. a stack walk reading live thread contexts, a live GC heap/root scan) passes
    /// <c>liveness: Liveness.AllValid</c> to run dump <em>and</em> live; everything else stays dump-only.
    /// Server GC and Mini dumps are likewise opt-in so the matrix doesn't explode.</para>
    ///
    /// <para>Each axis can be narrowed at run time by a comma-separated env allow-list:
    /// <c>SOSHARNESS_ONLY_HOSTS</c>, <c>_FLAVORS</c>, <c>_LIVENESS</c>, <c>_GCTYPE</c>, <c>_DUMPKIND</c>,
    /// <c>_COREVERSIONS</c> (e.g. <c>Net10,Net11</c>), <c>_DAC</c> (e.g. <c>Legacy</c>).</para>
    ///
    /// <para>Out-of-support Core versions are excluded from the default matrix; set
    /// <c>SOSHARNESS_TEST_OUT_OF_SUPPORT_CORE=1</c> to include them, or name them explicitly in
    /// <c>SOSHARNESS_ONLY_COREVERSIONS</c> (which bypasses the exclusion).</para></summary>
    public static TheoryData<TestConfig> BuildMatrix(
        string[] targets,
        Flavor flavor = Flavor.AllValid,
        Host host = Host.AllValid,
        Liveness liveness = Liveness.Dump,
        GcType gcType = GcType.Workstation,
        DumpKind dumpKind = DumpKind.Heap,
        CoreVersion coreVersion = CoreVersion.All,
        Dac dac = Dac.All)
    {
        TheoryData<TestConfig> data = new();
        foreach (TestConfig cfg in ApplyShardFilter(
            UnshardedPermutations(targets, flavor, host, liveness, gcType, dumpKind, coreVersion, dac)))
        {
            data.Add(cfg);
        }

        return data;
    }

    /// <summary>
    /// The raw valid configurations for the requested axes (what <see cref="BuildMatrix"/> wraps into a
    /// theory source). Exposed for theories that need to pair each config with an extra, non-axis column —
    /// e.g. a stop-point name — into their own <c>TheoryData&lt;TestConfig, ...&gt;</c>.
    /// </summary>
    public static IEnumerable<TestConfig> Permutations(
        string[] targets,
        Flavor flavor = Flavor.AllValid,
        Host host = Host.AllValid,
        Liveness liveness = Liveness.Dump,
        GcType gcType = GcType.Workstation,
        DumpKind dumpKind = DumpKind.Heap,
        CoreVersion coreVersion = CoreVersion.All,
        Dac dac = Dac.All) =>
        ApplyShardFilter(UnshardedPermutations(targets, flavor, host, liveness, gcType, dumpKind, coreVersion, dac));

    internal static IEnumerable<TestConfig> UnshardedPermutations(
        string[] targets,
        Flavor flavor = Flavor.AllValid,
        Host host = Host.AllValid,
        Liveness liveness = Liveness.Dump,
        GcType gcType = GcType.Workstation,
        DumpKind dumpKind = DumpKind.Heap,
        CoreVersion coreVersion = CoreVersion.All,
        Dac dac = Dac.All)
    {
        HashSet<TestConfig> seen = new();

        // Only ever expand versions the harness actually builds/installs; a requested bit outside the
        // available set is silently dropped (the axis disables, it never positively enables — see CoreVersion).
        CoreVersion requestedVersions = coreVersion & CoreVersions.Available;

        // Exclude out-of-support versions from the default matrix. They still run when opted in or when
        // explicitly named in SOSHARNESS_ONLY_COREVERSIONS (the explicit allow-list is authoritative).
        bool explicitVersions = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOSHARNESS_ONLY_COREVERSIONS"));
        if (!CoreVersions.TestOutOfSupportCore && !explicitVersions)
        {
            requestedVersions &= ~CoreVersions.OutOfSupport;
        }

        foreach (string target in targets)
        {
            foreach (Host h in SingleFlags(host, "SOSHARNESS_ONLY_HOSTS"))
            {
                foreach (Flavor f in SingleFlags(flavor, "SOSHARNESS_ONLY_FLAVORS"))
                {
                    foreach (Liveness l in SingleFlags(liveness, "SOSHARNESS_ONLY_LIVENESS"))
                    {
                        foreach (GcType g in SingleFlags(gcType, "SOSHARNESS_ONLY_GCTYPE"))
                        {
                            foreach (DumpKind d in SingleFlags(dumpKind, "SOSHARNESS_ONLY_DUMPKIND"))
                            {
                                foreach (CoreVersion cv in SingleFlags(requestedVersions, "SOSHARNESS_ONLY_COREVERSIONS"))
                                {
                                    foreach (Dac da in SingleFlags(dac, "SOSHARNESS_ONLY_DAC"))
                                    {
                                        // A live target has no dump kind. Collapse it to the canonical
                                        // value so we emit one live row, not one per dump-kind permutation.
                                        DumpKind dk = l == Liveness.Live ? DumpKind.Heap : d;

                                        // Desktop .NET Framework has no .NET Core version — the axis is inert
                                        // there. Collapse it to CoreVersion.None so every Framework row folds
                                        // into one (via the seen-set dedup below) instead of fanning out an
                                        // identical desktop-Framework config per Core version.
                                        CoreVersion cvEffective = f == Flavor.Framework ? CoreVersion.None : cv;

                                        TestConfig cfg = new(target, h, f, l, g, dk, cvEffective, da);
                                        if (IsValid(cfg) && seen.Add(cfg))
                                        {
                                            yield return cfg;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    internal static IEnumerable<TestConfig> ApplyShardFilter(IEnumerable<TestConfig> configs)
    {
        ShardSelection? shard = ShardSelection.FromEnvironment(Environment.GetEnvironmentVariable);
        foreach (TestConfig config in configs)
        {
            if (shard is null || config.GetCaptureShard(shard.Value.Count) == shard.Value.Index)
            {
                yield return config;
            }
        }
    }

    /// <summary>
    /// The immutable capture-family key used for sharding. Host and DAC are deliberately absent because
    /// they replay the same captured dump; keeping them together preserves <see cref="SnapshotStore"/> reuse.
    /// </summary>
    internal string CaptureFamilyKey =>
        $"{Target}|{Flavor}|{CoreVersion}|{GcType}|{DumpKind}|{Liveness}";

    internal int GetCaptureShard(int shardCount) =>
        (int)(StableHash(CaptureFamilyKey) % (ulong)shardCount);

    internal static ulong StableHash(string value)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        ulong hash = offsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash = unchecked(hash * prime);
        }

        return hash;
    }

    /// <summary>
    /// Whether a configuration is valid on the current platform. Centralizes every constraint that the old
    /// nested-loop <c>BuildMatrix</c> scattered across per-axis <c>continue</c>s.
    /// </summary>
    internal static bool IsValid(TestConfig c)
    {
        // Host platform constraints: cdb is Windows-only, lldb is non-Windows-only.
        if (c.Host == Host.Cdb && !OperatingSystem.IsWindows())
        {
            return false;
        }

        if (c.Host == Host.Lldb && OperatingSystem.IsWindows())
        {
            return false;
        }

        // Desktop .NET Framework is Windows-only.
        if (c.Flavor == Flavor.Framework && !OperatingSystem.IsWindows())
        {
            return false;
        }

        // SOS hosts cannot discover the statically linked CoreCLR module in musl single-file processes
        // or dumps. Keep Core coverage on Alpine while excluding unsupported single-file rows.
        if (!IsFlavorSupportedOnRid(c.Flavor, RepoLayout.Rid))
        {
            return false;
        }

        // dotnet-dump is post-mortem only; it has no live host.
        if (c.IsLive && c.Host == Host.DotnetDump)
        {
            return false;
        }

        // Live bpmd can't bind in a self-contained single-file image under the lldb host: CoreCLR is
        // statically linked into the symbol-stripped app image, so lldb has no symbol on which to set the
        // JIT/prestub notification breakpoint (.NET Core keeps CoreCLR as a distinct libcoreclr.so, so it
        // works there). Prune the (lldb, single-file, live) row for targets navigated via a managed stop
        // point; crash targets, which just run to the fault, keep their live single-file coverage.
        if (c.IsLive && c.Host == Host.Lldb && c.Flavor == Flavor.SingleFile && TargetCatalog.NavigatesViaBpmd(c.Target))
        {
            return false;
        }

        // A single-file snapshot requires a Full dump because createdump cannot enumerate reduced-dump
        // regions for a statically linked runtime. On constrained test machines, marker targets produce
        // several multi-gigabyte dumps and cannot complete reliably. Helix launchers can exclude only those
        // snapshot rows while preserving single-file crash coverage.
        if (!c.IsLive &&
            c.Flavor == Flavor.SingleFile &&
            TargetCatalog.NavigatesViaBpmd(c.Target) &&
            ExcludeSingleFileSnapshots(Environment.GetEnvironmentVariable("SOSHARNESS_EXCLUDE_SINGLEFILE_SNAPSHOTS")))
        {
            return false;
        }

        // The target must support the requested flavor (e.g. DynamicMethod can't build for Framework).
        if ((TargetCatalog.FlavorsFor(c.Target) & c.Flavor) == 0)
        {
            return false;
        }

        // Server GC is forced via .NET-Core GC env vars (DATAS off + fixed heap count); desktop .NET
        // Framework doesn't honor them, so Server is a Core/SingleFile-only axis.
        if (c.GcType == GcType.Server && c.Flavor == Flavor.Framework)
        {
            return false;
        }

        // Server GC for a LIVE target would require injecting the GC env vars into the dbgeng-launched
        // debuggee process; that isn't wired yet (no consumer), so Server is dump-only for now.
        if (c.GcType == GcType.Server && c.IsLive)
        {
            return false;
        }

        // Runtime createdump only supports full dumps for single-file apps when it needs the DAC to
        // enumerate reduced-dump regions. Don't generate Mini rows for single-file targets.
        if (c.DumpKind == DumpKind.Mini && c.Flavor == Flavor.SingleFile)
        {
            return false;
        }

        if (!IsDacSupported(c))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// The cDAC is available only for .NET Core 11+; desktop Framework and earlier Core versions use the
    /// legacy DAC. This is independent of host-platform constraints such as musl SingleFile support.
    /// </summary>
    internal static bool IsDacSupported(TestConfig config) =>
        config.Dac != Dac.CDac ||
        (config.Flavor != Flavor.Framework && (uint)config.CoreVersion >= (uint)CoreVersion.Net11);

    internal static bool IsFlavorSupportedOnRid(Flavor flavor, string rid) =>
        flavor != Flavor.SingleFile || !rid.StartsWith("linux-musl-", StringComparison.Ordinal);

    internal static bool ExcludeSingleFileSnapshots(string? value) => value switch
    {
        null or "" or "0" => false,
        "1" => true,
        _ => throw new InvalidOperationException(
            "SOSHARNESS_EXCLUDE_SINGLEFILE_SNAPSHOTS must be unset, 0, or 1."),
    };

    private static IEnumerable<T> SingleFlags<T>(T value) where T : struct, Enum
    {
        foreach (T candidate in Enum.GetValues<T>())
        {
            long v = Convert.ToInt64(candidate);
            if (v != 0 && (v & (v - 1)) == 0 && (Convert.ToInt64(value) & v) != 0)
            {
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// Like <see cref="SingleFlags{T}(T)"/>, but additionally narrowed by an optional comma-separated
    /// allow-list in <paramref name="envVar"/> (enum names, case-insensitive). Lets a run be staged onto a
    /// subset of the matrix during bring-up, e.g. <c>SOSHARNESS_ONLY_FLAVORS=Core</c>,
    /// <c>SOSHARNESS_ONLY_HOSTS=Cdb,DotnetDump</c>, <c>SOSHARNESS_ONLY_GCTYPE=Server</c>.
    /// </summary>
    private static IEnumerable<T> SingleFlags<T>(T value, string envVar) where T : struct, Enum
    {
        string? only = Environment.GetEnvironmentVariable(envVar);
        HashSet<string>? allowed = string.IsNullOrEmpty(only)
            ? null
            : new HashSet<string>(only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

        foreach (T candidate in SingleFlags(value))
        {
            if (allowed is null || allowed.Contains(candidate.ToString()))
            {
                yield return candidate;
            }
        }
    }

    void IXunitSerializable.Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Target), Target, typeof(string));
        info.AddValue(nameof(Host), Host, typeof(Host));
        info.AddValue(nameof(Flavor), Flavor, typeof(Flavor));
        info.AddValue(nameof(Liveness), Liveness, typeof(Liveness));
        info.AddValue(nameof(GcType), GcType, typeof(GcType));
        info.AddValue(nameof(DumpKind), DumpKind, typeof(DumpKind));
        info.AddValue(nameof(CoreVersion), CoreVersion, typeof(CoreVersion));
        info.AddValue(nameof(Dac), Dac, typeof(Dac));
    }

    void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
    {
        _target = info.GetValue<string>(nameof(Target))!;
        _host = info.GetValue<Host>(nameof(Host));
        _flavor = info.GetValue<Flavor>(nameof(Flavor));
        _liveness = info.GetValue<Liveness>(nameof(Liveness));
        _gcType = info.GetValue<GcType>(nameof(GcType));
        _dumpKind = info.GetValue<DumpKind>(nameof(DumpKind));
        _coreVersion = info.GetValue<CoreVersion>(nameof(CoreVersion));
        _dac = info.GetValue<Dac>(nameof(Dac));
    }

    /// <summary>
    /// A legible, deterministic id used for the theory display name and de-duplication, e.g.
    /// <c>scenarios/Cdb/Core/net10/Dump/Workstation/Heap</c> (the runtime version is always shown; the dump
    /// kind is omitted for live rows; a <c>/cdac</c> suffix marks the cDAC variant. Legacy DAC is the
    /// implicit default and isn't tokenized, so single-DAC ids stay terse.
    /// </summary>
    public override string ToString()
    {
        string version = CoreVersion == CoreVersion.None ? "/netfx" : "/net" + CoreVersions.Major(CoreVersion);
        string dump = IsLive ? string.Empty : "/" + DumpKind;
        string dac = Dac == Dac.CDac ? "/cdac" : string.Empty;
        return $"{Target}/{Host}/{Flavor}{version}/{Liveness}/{GcType}{dump}{dac}";
    }

}

internal readonly record struct ShardSelection(int Index, int Count)
{
    private const string IndexVariable = "SOSHARNESS_SHARD_INDEX";
    private const string CountVariable = "SOSHARNESS_SHARD_COUNT";

    public static ShardSelection? FromEnvironment(Func<string, string?> getEnvironmentVariable)
    {
        string? indexValue = getEnvironmentVariable(IndexVariable);
        string? countValue = getEnvironmentVariable(CountVariable);

        if (indexValue is null && countValue is null)
        {
            return null;
        }

        if (indexValue is null || countValue is null)
        {
            throw new InvalidOperationException(
                $"{IndexVariable} and {CountVariable} must either both be set or both be unset.");
        }

        if (!int.TryParse(countValue, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count <= 0)
        {
            throw new InvalidOperationException(
                $"{CountVariable} must be a positive base-10 integer; received '{countValue}'.");
        }

        if (!int.TryParse(indexValue, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
            index < 0 ||
            index >= count)
        {
            throw new InvalidOperationException(
                $"{IndexVariable} must be a base-10 integer in [0, {count}); received '{indexValue}'.");
        }

        return new ShardSelection(index, count);
    }
}
