// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace SOS.TestHarness;

/// <summary>
/// Maps the <see cref="CoreVersion"/> matrix axis onto the runtimes defined by the repo build:
/// the configured set (<see cref="Available"/>), each version's target framework moniker
/// (<see cref="Tfm"/>), and — for the self-contained single-file DAC lookup — its exact runtime patch
/// version (<see cref="RuntimeVersion"/>).
///
/// <para>The source of truth is <c>RuntimeTestVersions</c> from <c>eng/Versions.props</c>, embedded in
/// this assembly as exact runtime versions. Target frameworks are derived from each version's major
/// number.</para>
/// </summary>
public static class CoreVersions
{
    // tfm major -> exact runtime version, parsed once from the build-defined version metadata.
    private static readonly IReadOnlyDictionary<int, string> s_runtimeVersions = ReadConfiguredVersions();

    /// <summary>
    /// The versions the harness actually builds debuggees for and has runtimes installed for. The matrix
    /// only ever runs versions in this set; a requested <see cref="CoreVersion"/> bit outside it is
    /// silently dropped (the axis is a disable mechanism — see <see cref="CoreVersion"/>).
    /// </summary>
    public static CoreVersion Available { get; } = ComputeAvailable();

    /// <summary>
    /// The .NET Core versions that are out of support. They are excluded from the default test matrix
    /// (see <see cref="TestConfig.Permutations"/>) and only run when opted in via
    /// <see cref="TestOutOfSupportCore"/> or explicitly named in
    /// <c>SOSHARNESS_ONLY_COREVERSIONS</c>.
    /// </summary>
    public static CoreVersion OutOfSupport => CoreVersion.Net9;

    /// <summary>
    /// Whether out-of-support versions are opted into the default matrix, via
    /// <c>SOSHARNESS_TEST_OUT_OF_SUPPORT_CORE=1</c>.
    /// </summary>
    public static bool TestOutOfSupportCore =>
        Environment.GetEnvironmentVariable("SOSHARNESS_TEST_OUT_OF_SUPPORT_CORE") == "1";

    /// <summary>
    /// The <c>net*.0</c> target framework moniker for a single <see cref="CoreVersion"/> bit, or
    /// <c>netfx</c> for <see cref="CoreVersion.None"/> (the desktop .NET Framework flavor, which has no Core
    /// version — used as a stable dump/output folder segment).
    /// </summary>
    public static string Tfm(CoreVersion version) => version == CoreVersion.None ? "netfx" : $"net{Major(version)}.0";

    /// <summary>The major version number (8, 9, 10, ...) for a single <see cref="CoreVersion"/> bit.</summary>
    public static int Major(CoreVersion version)
    {
        uint v = (uint)version;
        if (v == 0 || (v & (v - 1)) != 0)
        {
            throw new ArgumentException($"Expected a single CoreVersion flag, got '{version}'.", nameof(version));
        }

        return System.Numerics.BitOperations.Log2(v);
    }

    /// <summary>
    /// The exact configured runtime patch version (e.g. <c>8.0.25</c> or
    /// <c>11.0.0-preview.6.26318.108</c>). Returns <c>null</c> if that version wasn't defined.
    /// </summary>
    public static string? RuntimeVersion(CoreVersion version) =>
        s_runtimeVersions.TryGetValue(Major(version), out string? v) ? v : null;

    private static CoreVersion ComputeAvailable()
    {
        CoreVersion configured = 0;
        foreach (int major in s_runtimeVersions.Keys)
        {
            configured |= (CoreVersion)(1u << major);
        }

        return configured != 0
            ? configured
            : throw new InvalidOperationException("No SOS test runtime versions were embedded by the build.");
    }

    private static IReadOnlyDictionary<int, string> ReadConfiguredVersions()
    {
        Dictionary<int, string> map = new();
        string configuredVersions = typeof(CoreVersions).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "SOS.TestHarness.RuntimeVersions")
            .Value ?? string.Empty;
        foreach (string entry in configuredVersions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = entry.IndexOf('.');
            if (separator <= 0 ||
                !int.TryParse(entry.AsSpan(0, separator), out int major))
            {
                throw new InvalidOperationException($"Invalid embedded SOS test runtime version '{entry}'.");
            }

            if (!map.TryAdd(major, entry))
            {
                throw new InvalidOperationException($"Multiple SOS test runtime versions were configured for .NET {major}.");
            }
        }

        return map;
    }
}
