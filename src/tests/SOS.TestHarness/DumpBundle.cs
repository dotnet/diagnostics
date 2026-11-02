// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Diagnostics.Runtime;

namespace SOS.TestHarness;

/// <summary>A relocatable collection of dumps, debuggee images, symbols, and matching runtime files.</summary>
public sealed class DumpBundle
{
    internal const string ManifestFileName = "dumps.json";
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _root;
    private readonly Dictionary<DumpKey, DumpBundleEntry> _entries;
    private readonly HashSet<string> _files;

    public TargetPlatform Platform { get; }
    internal CoreVersion AvailableCoreVersions => _entries.Keys.Aggregate(CoreVersion.None, (versions, key) => versions | key.CoreVersion);

    private DumpBundle(string root, DumpBundleManifest manifest)
    {
        _root = Path.GetFullPath(root);
        if (manifest.FormatVersion != 1 || manifest.Entries.Count == 0 || manifest.Files.Count == 0)
        {
            throw new InvalidDataException("A dump bundle must have format version 1 and nonempty entries and files.");
        }

        Platform = new(manifest.SourceRid);
        _entries = new();
        _files = manifest.Files.ToHashSet(StringComparer.Ordinal);
        foreach (string file in manifest.Files)
        {
            RequireFile(file);
        }

        foreach (DumpBundleEntry entry in manifest.Entries)
        {
            DumpKey key = entry.Key;
            TargetDefinition target = TargetCatalog.Get(key.Target);
            uint version = (uint)key.CoreVersion;
            if (key.Flavor is not (Flavor.Core or Flavor.SingleFile or Flavor.Framework) ||
                key.GcType is not (GcType.Workstation or GcType.Server) ||
                key.DumpKind is not (DumpKind.Heap or DumpKind.Mini or DumpKind.Full) ||
                (target.Flavors & key.Flavor) == 0 || target.StopPoints.Count == 0 ||
                (key.Flavor == Flavor.Framework ? key.CoreVersion != CoreVersion.None :
                    version == 0 || (version & (version - 1)) != 0 || !Enum.IsDefined(key.CoreVersion)) ||
                string.IsNullOrWhiteSpace(entry.RuntimeVersion) ||
                (key.Flavor != Flavor.Framework &&
                 !entry.RuntimeVersion.StartsWith(CoreVersions.Major(key.CoreVersion) + ".", StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"Invalid dump bundle configuration '{key}'.");
            }

            if (!_entries.TryAdd(key, entry))
            {
                throw new InvalidDataException($"Duplicate dump bundle configuration '{key}'.");
            }

            foreach (StopPoint stop in target.StopPoints)
            {
                Resolve(key, stop.Name);
            }
        }
    }

    public static DumpBundle Load(string root)
    {
        string manifest = Path.Combine(Path.GetFullPath(root), ManifestFileName);
        return new(root, JsonSerializer.Deserialize<DumpBundleManifest>(File.ReadAllText(manifest), JsonOptions)
            ?? throw new InvalidDataException($"Empty dump bundle manifest '{manifest}'."));
    }

    internal DumpArtifact Resolve(DumpKey key, string stop)
    {
        TargetCatalog.Get(key.Target).Stop(stop);
        if (!_entries.TryGetValue(key, out DumpBundleEntry? entry))
        {
            throw new FileNotFoundException($"Dump bundle '{_root}' has no {key} at stop '{stop}'.");
        }

        string runtime = ResolvePath(entry.RuntimeDirectory);
        if (!Directory.Exists(runtime))
        {
            throw new DirectoryNotFoundException($"Dump bundle runtime directory '{runtime}' was not found.");
        }

        return new(
            RequireFile(entry.DumpDirectory + "/" + stop + ".dmp"),
            RequireFile(entry.ExecutablePath),
            runtime);
    }

    private string RequireFile(string relative)
    {
        if (!_files.Contains(relative))
        {
            throw new InvalidDataException($"Dump bundle input '{relative}' is missing from the file inventory.");
        }

        string path = ResolvePath(relative);
        return File.Exists(path) ? path :
            throw new FileNotFoundException($"Required dump bundle input was not found at '{path}'.", path);
    }

    /// <summary>Capture selected configurations and export their files without running SOS assertions.</summary>
    public static void Capture(string outputDirectory, IEnumerable<TestConfig> configurations)
    {
        if (DumpStore.IsReadOnly)
        {
            throw new InvalidOperationException("Cannot capture dumps with SOSHARNESS_DUMP_INPUT configured.");
        }

        DumpKey[] keys = configurations.Where(config => !config.IsLive && TargetCatalog.Get(config.Target).StopPoints.Count > 0)
            .Select(DumpKey.From).Distinct().ToArray();
        if (keys.Length == 0)
        {
            throw new ArgumentException("No dump configurations were selected.", nameof(configurations));
        }

        string root = Path.GetFullPath(outputDirectory);
        if (Path.Exists(root))
        {
            throw new IOException($"Dump bundle output '{root}' already exists.");
        }

        Directory.CreateDirectory(root);
        List<DumpBundleEntry> entries = new();
        HashSet<string> files = new(StringComparer.Ordinal);
        HashSet<string> copiedDirectories = new(StringComparer.Ordinal);
        foreach (DumpKey key in keys)
        {
            TargetDefinition target = TargetCatalog.Get(key.Target);
            foreach (StopPoint stop in target.StopPoints)
            {
                SnapshotStore.CaptureDump(key.Flavor, key.Target, stop.Name, key.GcType, key.DumpKind, key.CoreVersion);
            }

            string exe = SnapshotStore.TargetExe(key.Flavor, key.Target, key.CoreVersion);
            string tfm = CoreVersions.Tfm(key.CoreVersion);
            string debuggee = $"debuggees/{key.Flavor}/{tfm}/{target.Project}";
            string runtimeSource = RuntimeDirectory(key, SnapshotStore.CaptureDump(
                key.Flavor, key.Target, target.DefaultStopName, key.GcType, key.DumpKind, key.CoreVersion));
            string version = key.Flavor == Flavor.Framework
                ? FileVersionInfo.GetVersionInfo(Path.Combine(runtimeSource, "clr.dll")).FileVersion
                    ?? throw new InvalidDataException($"Cannot determine CLR version in '{runtimeSource}'.")
                : key.Flavor == Flavor.Core ? Path.GetFileName(runtimeSource) : CoreVersions.RuntimeVersion(key.CoreVersion)
                    ?? throw new InvalidDataException($"No runtime version configured for '{key.CoreVersion}'.");
            string runtime = $"runtimes/{key.Flavor}/{(key.Flavor == Flavor.Framework ? tfm : version)}";
            string dumps = "dumps/" + key.RelativeDirectory;

            CopyDirectory(Path.GetDirectoryName(exe)!, debuggee);
            CopyDirectory(runtimeSource, runtime);
            CopyDirectory(Path.Combine(DumpStore.OutputDirectory, key.RelativeDirectory), dumps);
            entries.Add(new(key, dumps, debuggee + "/" + Path.GetFileName(exe), runtime, version));
            Console.WriteLine($"Captured {key} into '{root}'.");
        }

        DumpBundleManifest manifest = new(1, RepoLayout.Rid, entries, files.Order(StringComparer.Ordinal).ToArray());
        File.WriteAllText(Path.Combine(root, ManifestFileName), JsonSerializer.Serialize(manifest, JsonOptions));
        _ = Load(root);

        void CopyDirectory(string source, string destination)
        {
            if (!copiedDirectories.Add(destination))
            {
                return;
            }

            if (Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new IOException("A dump bundle cannot be exported inside its source directory.");
            }

            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                if (destination.StartsWith("runtimes/", StringComparison.Ordinal) &&
                    Path.GetFileName(file).Contains("_universal", StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = destination + "/" + Path.GetRelativePath(source, file).Replace('\\', '/');
                string target = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
                files.Add(relative);
            }
        }
    }

    private static string RuntimeDirectory(DumpKey key, string dumpPath)
    {
        if (key.Flavor == Flavor.SingleFile)
        {
            return ToolPaths.SingleFileDacDirectory(key.CoreVersion)
                ?? throw new DirectoryNotFoundException($"No matching runtime files for '{key.CoreVersion}'.");
        }

        using DataTarget target = DataTarget.LoadDump(dumpPath);
        string module = target.ClrVersions.Single().ModuleInfo.FileName;
        if (!File.Exists(module))
        {
            throw new FileNotFoundException($"The captured runtime image '{module}' is unavailable on the producer.", module);
        }
        return Path.GetDirectoryName(module)!;
    }

    private string ResolvePath(string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"Dump bundle path '{relative}' must be a portable relative path.");
        }

        return Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}

internal sealed record DumpBundleManifest(int FormatVersion, string SourceRid, IReadOnlyList<DumpBundleEntry> Entries, IReadOnlyList<string> Files);
internal sealed record DumpBundleEntry(DumpKey Key, string DumpDirectory, string ExecutablePath, string RuntimeDirectory, string RuntimeVersion);
