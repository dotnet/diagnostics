// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Maps the <see cref="CoreVersion"/> matrix axis onto the concrete runtimes the repo build installed:
/// the set actually present (<see cref="Available"/>), each version's target framework moniker
/// (<see cref="Tfm"/>), and — for the self-contained single-file DAC lookup — its exact runtime patch
/// version (<see cref="RuntimeVersion"/>).
///
/// <para>The source of truth is <c>artifacts/dotnet-test/Debugger.Tests.Versions.txt</c>, the manifest
/// <c>eng/InstallRuntimes.proj</c> writes when it acquires the <c>RuntimeTestVersions</c> (8/9/10/11). If
/// the manifest isn't present (runtimes not yet installed), <see cref="Available"/> falls back to the
/// versions in <c>Directory.Build.props</c>'s <c>SupportedSubProcessTargetFrameworks</c>.</para>
/// </summary>
public static class CoreVersions
{
    // tfm major -> exact runtime version, parsed once from the install manifest.
    private static readonly IReadOnlyDictionary<int, string> s_runtimeVersions = ReadManifest();

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
    /// The exact runtime patch version (e.g. <c>8.0.25</c>, <c>11.0.0-preview.6.26318.108</c>) for a
    /// version, from the install manifest. Returns <c>null</c> if that version wasn't installed.
    /// </summary>
    public static string? RuntimeVersion(CoreVersion version) =>
        s_runtimeVersions.TryGetValue(Major(version), out string? v) ? v : null;

    private static CoreVersion ComputeAvailable()
    {
        // Prefer what's actually installed (the manifest); fall back to the props-declared set.
        CoreVersion fromManifest = 0;
        foreach (int major in s_runtimeVersions.Keys)
        {
            fromManifest |= (CoreVersion)(1u << major);
        }

        return fromManifest != 0 ? fromManifest : ReadSupportedFrameworksFromProps();
    }

    private static IReadOnlyDictionary<int, string> ReadManifest()
    {
        Dictionary<int, string> map = new();
        string manifest = Path.Combine(RepoLayout.Root, "artifacts", "dotnet-test", "Debugger.Tests.Versions.txt");
        if (!File.Exists(manifest))
        {
            return map;
        }

        // The manifest pairs <TargetFramework{Slot}>net{N}.0</> with <RuntimeVersion{Slot}>{version}</> for
        // each slot (Latest, Servicing1, ...). Collect both, then join on slot.
        Dictionary<string, string> tfms = new(StringComparer.OrdinalIgnoreCase);    // slot -> netN.0
        Dictionary<string, string> versions = new(StringComparer.OrdinalIgnoreCase); // slot -> version
        foreach (string line in File.ReadLines(manifest))
        {
            CollectTagged(line, "TargetFramework", tfms);
            CollectTagged(line, "RuntimeVersion", versions);
        }

        foreach ((string slot, string tfm) in tfms)
        {
            if (versions.TryGetValue(slot, out string? version) &&
                tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
                tfm.EndsWith(".0", StringComparison.Ordinal) &&
                int.TryParse(tfm.AsSpan(3, tfm.Length - 5), out int major))
            {
                map[major] = version;
            }
        }

        return map;
    }

    /// <summary>If <paramref name="line"/> is <c>&lt;{prefix}{slot}&gt;value&lt;/...&gt;</c>, record slot-&gt;value.</summary>
    private static void CollectTagged(string line, string prefix, Dictionary<string, string> into)
    {
        string open = $"<{prefix}";
        int start = line.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
        {
            return;
        }

        int slotStart = start + open.Length;
        int slotEnd = line.IndexOf('>', slotStart);
        if (slotEnd < 0)
        {
            return;
        }

        string slot = line.Substring(slotStart, slotEnd - slotStart);
        int valEnd = line.IndexOf("</", slotEnd, StringComparison.Ordinal);
        if (valEnd < 0)
        {
            return;
        }

        into[slot] = line.Substring(slotEnd + 1, valEnd - slotEnd - 1).Trim();
    }

    private static CoreVersion ReadSupportedFrameworksFromProps()
    {
        CoreVersion result = 0;
        string props = Path.Combine(RepoLayout.Root, "Directory.Build.props");
        if (File.Exists(props))
        {
            foreach (string line in File.ReadLines(props))
            {
                const string tag = "<SupportedSubProcessTargetFrameworks>";
                int open = line.IndexOf(tag, StringComparison.Ordinal);
                if (open < 0)
                {
                    continue;
                }

                int close = line.IndexOf("</", open, StringComparison.Ordinal);
                string value = line.Substring(open + tag.Length, close - open - tag.Length);
                foreach (string tfm in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
                        tfm.EndsWith(".0", StringComparison.Ordinal) &&
                        int.TryParse(tfm.AsSpan(3, tfm.Length - 5), out int major))
                    {
                        result |= (CoreVersion)(1u << major);
                    }
                }

                break;
            }
        }

        // Last-ditch default matching the current servicing+preview set.
        return result != 0 ? result : CoreVersion.Net8 | CoreVersion.Net9 | CoreVersion.Net10 | CoreVersion.Net11;
    }
}
