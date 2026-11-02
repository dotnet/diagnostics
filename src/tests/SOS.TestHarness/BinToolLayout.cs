// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

internal sealed class BinToolLayout : IToolLayout
{
    private readonly Lazy<ToolCommand> _dotNetDump = new(ResolveDotNetDump);
    private readonly Lazy<string?> _cdacOverrideDirectory = new(ResolveCDacOverrideDirectory);
    public string SosDirectory => RepoLayout.ArtifactsBinNative;
    public string SosAssemblyPath => typeof(SOS.Extensions.HostServices).Assembly.Location;
    public ToolCommand DotNetDump => _dotNetDump.Value;
    public string DumpCacheDirectory => Path.Combine(RepoLayout.Scratch, "dumps");
    public string? BundleExtractionDirectory => null;
    public string? CDacOverrideDirectory => _cdacOverrideDirectory.Value;

    private static ToolCommand ResolveDotNetDump()
    {
        string baseDir = Path.Combine(RepoLayout.ArtifactsBin, "dotnet-dump", RepoLayout.ArtifactsConfiguration, "net8.0");
        string published = Path.Combine(baseDir, "publish", "dotnet-dump.dll");
        string dll = File.Exists(published) ? published : ToolLayout.RequireFile(Path.Combine(baseDir, "dotnet-dump.dll"));
        return new(RepoLayout.DotNetExe, dll);
    }

    private static string? ResolveCDacOverrideDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable("SOSHARNESS_CDAC_DIR");
        string directory = string.IsNullOrEmpty(configured)
            ? Path.Combine(RepoLayout.Root, "artifacts", "cdac-override", RepoLayout.ArtifactsConfiguration)
            : configured;

        return File.Exists(Path.Combine(directory, CDacOverride.DacFileName)) ? directory : null;
    }
}
