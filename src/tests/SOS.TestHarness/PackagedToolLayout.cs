// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>The installed SOS layout shared by NuGet tools and single-file bundles.</summary>
internal abstract class PackagedToolLayout : IToolLayout
{
    private readonly Lazy<string?> _cdacOverrideDirectory = new(ValidateCDacOverride);

    protected string Root { get; }
    public string SosDirectory => Path.Combine(Root, "sos");
    public string SosAssemblyPath => Path.Combine(SosDirectory, "net8.0", "SOS.Extensions.dll");
    public ToolCommand DotNetDump { get; }
    public string DumpCacheDirectory => Path.Combine(Root, "test-artifacts", "dumps");
    public virtual string? BundleExtractionDirectory => null;
    public string? CDacOverrideDirectory => _cdacOverrideDirectory.Value;

    protected PackagedToolLayout(string root)
    {
        Root = Path.GetFullPath(root);
        string nativeSosName = OperatingSystem.IsWindows() ? "sos.dll" :
            OperatingSystem.IsMacOS() ? "libsosplugin.dylib" : "libsosplugin.so";
        ToolLayout.RequireFile(Path.Combine(SosDirectory, nativeSosName));
        ToolLayout.RequireFile(SosAssemblyPath);
        DotNetDump = new(ToolLayout.RequireFile(Path.Combine(Root, "tools", "dotnet-dump" + RepoLayout.ExeSuffix)));
    }

    private static string? ValidateCDacOverride()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOSHARNESS_CDAC_DIR")))
        {
            throw new InvalidOperationException("SOSHARNESS_CDAC_DIR cannot override a packaged SOS test layout.");
        }
        return null;
    }
}
