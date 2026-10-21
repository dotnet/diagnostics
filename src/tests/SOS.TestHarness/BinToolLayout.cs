// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;

namespace SOS.TestHarness;

internal sealed class BinToolLayout : IToolLayout
{
    private readonly Lazy<ToolCommand> _dotNetDump = new(ResolveDotNetDump);
    private readonly Lazy<string?> _cdacOverrideDirectory = new(ResolveCDacOverrideDirectory);
    private readonly object _cdacCopyLock = new();
    private readonly HashSet<string> _cdacOverrideDestinations = new(StringComparer.OrdinalIgnoreCase);

    public string SosDirectory => RepoLayout.ArtifactsBinNative;
    public string ManagedSosDirectory => Path.Combine(SosDirectory, "net8.0");
    public string DumpCacheDirectory => Path.Combine(RepoLayout.Scratch, "dumps");
    public string? CDacOverrideDirectory => _cdacOverrideDirectory.Value;

    public ProcessStartInfo CreateDotNetDumpStartInfo() => _dotNetDump.Value.CreateStartInfo();

    public void ConfigureCaptureEnvironment(ProcessStartInfo debuggee) => _dotNetDump.Value.ConfigureCaptureEnvironment(debuggee);

    public Assembly LoadSosAssembly() => typeof(SOS.Extensions.HostServices).Assembly;

    public void PrepareCDacOverride(Host host)
    {
        string? overrideDirectory = CDacOverrideDirectory;
        if (overrideDirectory is null)
        {
            return;
        }

        string destinationDirectory = host switch
        {
            Host.Cdb or Host.Lldb => SosDirectory,
            Host.DotnetDump => Path.Combine(Path.GetDirectoryName(_dotNetDump.Value.AssemblyPath)!, RepoLayout.Rid),
            _ => throw new ArgumentException($"Unknown host '{host}'.", nameof(host)),
        };

        lock (_cdacCopyLock)
        {
            if (_cdacOverrideDestinations.Contains(destinationDirectory))
            {
                return;
            }

            CopyOverrideFile(CDacFileName);
            CopyOverrideFile(CDbiFileName);
            _cdacOverrideDestinations.Add(destinationDirectory);

            void CopyOverrideFile(string fileName)
            {
                string source = Path.Combine(overrideDirectory, fileName);
                string destination = Path.Combine(destinationDirectory, fileName);
                if (!File.Exists(destination) || !FilesEqual(source, destination))
                {
                    File.Copy(source, destination, overwrite: true);
                }
            }
        }
    }

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

        return File.Exists(Path.Combine(directory, CDacFileName)) ? directory : null;
    }

    private static bool FilesEqual(string leftPath, string rightPath)
    {
        FileInfo leftInfo = new(leftPath);
        FileInfo rightInfo = new(rightPath);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using FileStream left = File.OpenRead(leftPath);
        using FileStream right = File.OpenRead(rightPath);
        int leftByte;
        while ((leftByte = left.ReadByte()) != -1)
        {
            if (leftByte != right.ReadByte())
            {
                return false;
            }
        }
        return right.ReadByte() == -1;
    }

    private static string CDacFileName =>
        OperatingSystem.IsWindows() ? "mscordaccore_universal.dll" :
        OperatingSystem.IsMacOS() ? "libmscordaccore_universal.dylib" : "libmscordaccore_universal.so";

    private static string CDbiFileName =>
        OperatingSystem.IsWindows() ? "mscordbi_universal.dll" :
        OperatingSystem.IsMacOS() ? "libmscordbi_universal.dylib" : "libmscordbi_universal.so";
}
