// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace SOS.TestHarness;

/// <summary>The installed SOS layout shared by NuGet tools and single-file bundles.</summary>
internal abstract class PackagedToolLayout : IToolLayout
{
    private readonly ToolCommand _dotNetDump;
    private readonly Lazy<Assembly> _sosAssembly;
    private readonly Lazy<string?> _cdacOverrideDirectory = new(ValidateCDacOverride);

    protected string Root { get; }
    public string SosDirectory => Path.Combine(Root, "sos");
    public string ManagedSosDirectory => Path.Combine(SosDirectory, "net8.0");
    public string DumpCacheDirectory => Path.Combine(Root, "test-artifacts", "dumps");
    public string? CDacOverrideDirectory => _cdacOverrideDirectory.Value;

    protected PackagedToolLayout(string root)
    {
        Root = Path.GetFullPath(root);
        string nativeSosName = OperatingSystem.IsWindows() ? "sos.dll" :
            OperatingSystem.IsMacOS() ? "libsosplugin.dylib" : "libsosplugin.so";
        ToolLayout.RequireFile(Path.Combine(SosDirectory, nativeSosName));
        ToolLayout.RequireFile(Path.Combine(ManagedSosDirectory, "SOS.Extensions.dll"));
        _dotNetDump = new(ToolLayout.RequireFile(Path.Combine(Root, "tools", "dotnet-dump" + RepoLayout.ExeSuffix)));
        _sosAssembly = new(() => new SosLoadContext(ManagedSosDirectory)
            .LoadFromAssemblyPath(Path.Combine(ManagedSosDirectory, "SOS.Extensions.dll")));
    }

    public ProcessStartInfo CreateDotNetDumpStartInfo()
    {
        ProcessStartInfo startInfo = _dotNetDump.CreateStartInfo();
        ConfigureEnvironment(startInfo);
        return startInfo;
    }

    public void ConfigureCaptureEnvironment(ProcessStartInfo debuggee)
    {
        _dotNetDump.ConfigureCaptureEnvironment(debuggee);
        ConfigureEnvironment(debuggee);
    }

    public Assembly LoadSosAssembly() => _sosAssembly.Value;

    public void PrepareCDacOverride(Host host)
    {
        _ = CDacOverrideDirectory;
    }

    protected virtual void ConfigureEnvironment(ProcessStartInfo startInfo)
    {
    }

    private static string? ValidateCDacOverride()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOSHARNESS_CDAC_DIR")))
        {
            throw new InvalidOperationException("SOSHARNESS_CDAC_DIR cannot override a packaged SOS test layout.");
        }
        return null;
    }

    // Keep the packaged product closure separate from the build-output assemblies referenced by the harness.
    internal sealed class SosLoadContext(string directory) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly HashSet<string> _frameworkAssemblies = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null && _frameworkAssemblies.Contains(assemblyName.Name))
            {
                return null;
            }

            string path = ToolLayout.RequireFile(Path.Combine(directory, assemblyName.Name + ".dll"));
            Console.Error.WriteLine($"Packaged SOS assembly: {path}");
            return LoadFromAssemblyPath(path);
        }
    }
}
