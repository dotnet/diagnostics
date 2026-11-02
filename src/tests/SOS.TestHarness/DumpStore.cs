// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace SOS.TestHarness;

/// <summary>Dump inputs and capture storage, independent of the product layout under test.</summary>
public static class DumpStore
{
    private static readonly string? s_inputRoot = Environment.GetEnvironmentVariable("SOSHARNESS_DUMP_INPUT");
    private static readonly Lazy<DumpBundle?> s_input = new(() =>
        s_inputRoot is { Length: > 0 } root
            ? DumpBundle.Load(root)
            : null);
    private static readonly Lazy<TargetPlatform> s_analyzer = new(() => new(RepoLayout.Rid));
    private static readonly Lazy<string> s_output = new(() =>
        Environment.GetEnvironmentVariable("SOSHARNESS_DUMP_OUTPUT") is { Length: > 0 } root
            ? Path.GetFullPath(root)
            : Path.Combine(RepoLayout.Scratch, "dumps"));

    internal static DumpBundle? Input => s_input.Value;
    internal static TargetPlatform AnalyzerPlatform => s_analyzer.Value;
    public static bool IsReadOnly => s_inputRoot is { Length: > 0 };
    public static TargetPlatform SourcePlatform => Input?.Platform ?? AnalyzerPlatform;

    public static string OutputDirectory => s_output.Value;

    internal static DumpArtifact Resolve(TestConfig config, string stop)
    {
        if (config.IsLive)
        {
            throw new ArgumentException("Dump inputs cannot resolve a live target.", nameof(config));
        }

        if (Input is { } bundle)
        {
            if (!bundle.Platform.Supports(config, AnalyzerPlatform) ||
                (bundle.Platform.Rid != AnalyzerPlatform.Rid &&
                 !string.Equals(DacPolicy.UseCDac(config.Dac), "true", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Analyzing {bundle.Platform.Rid} dumps on {AnalyzerPlatform.Rid} requires dotnet-dump with .NET 11+ cDAC.");
            }

            return bundle.Resolve(DumpKey.From(config), stop);
        }

        return new(SnapshotStore.CaptureDump(config.Flavor, config.Target, stop, config.GcType, config.DumpKind, config.CoreVersion),
            SnapshotStore.TargetExe(config.Flavor, config.Target, config.CoreVersion));
    }
}

internal sealed record DumpKey(Flavor Flavor, string Target, GcType GcType, DumpKind DumpKind, CoreVersion CoreVersion)
{
    public static DumpKey From(TestConfig config) =>
        new(config.Flavor, config.Target, config.GcType, config.DumpKind, config.CoreVersion);

    [JsonIgnore]
    public string RelativeDirectory =>
        $"{Flavor.ToString().ToLowerInvariant()}/{GcType.ToString().ToLowerInvariant()}/{DumpKind.ToString().ToLowerInvariant()}/{CoreVersions.Tfm(CoreVersion)}/{Target}";
}

public sealed record DumpArtifact(string DumpPath, string ExecutablePath, string? RuntimeDirectory = null)
{
    public IEnumerable<string> SymbolDirectories => RuntimeDirectory is null
        ? []
        : [Path.GetDirectoryName(ExecutablePath)!, RuntimeDirectory];

    public void ConfigureSymbols(IDebuggerHost host, Dac dac)
    {
        foreach (string directory in SymbolDirectories)
        {
            host.Sos($"setsymbolserver -directory \"{directory}\"");
        }

        if (RuntimeDirectory is not null && dac == Dac.Legacy)
        {
            host.Sos($"setclrpath {RuntimeDirectory}");
        }
    }
}
