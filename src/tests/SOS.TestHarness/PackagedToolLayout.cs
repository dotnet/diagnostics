// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>The SOS layout installed from NuGet tools.</summary>
internal sealed class PackagedToolLayout : IToolLayout
{
    private readonly Lazy<string?> _cdacOverrideDirectory = new(ValidateCDacOverride);

    private string Root { get; }
    public string SosDirectory => Path.Combine(Root, "sos");
    public string SosAssemblyPath => Path.Combine(SosDirectory, "net8.0", "SOS.Extensions.dll");
    public ToolCommand DotNetDump { get; }
    public string DumpCacheDirectory => Path.Combine(Root, "test-artifacts", "dumps");
    public string? CDacOverrideDirectory => _cdacOverrideDirectory.Value;

    public PackagedToolLayout(string root)
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
