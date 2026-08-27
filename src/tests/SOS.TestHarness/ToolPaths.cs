// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Resolves the external tools the harness drives, pointing every one at the diagnostics repo's
/// own build outputs (so the harness always validates freshly-built SOS, not a stale machine-wide
/// install):
/// <list type="bullet">
///   <item><c>dbgeng.dll</c> comes from the restored <c>cdb-sos</c> package, so no WinDbg installation
///   is required.</item>
///   <item>Native <c>sos.dll</c> comes from the repo's native build output.</item>
///   <item><c>dotnet-dump</c> is the repo-built tool, run as <c>dotnet dotnet-dump.dll</c>.</item>
/// </list>
/// </summary>
public static class ToolPaths
{
    /// <summary>
    /// Directory containing <c>dbgeng.dll</c>, taken from the restored <c>cdb-sos</c> package's
    /// <c>runtimes/win-&lt;arch&gt;/native</c> directory. Windows-only; resolved lazily so non-Windows
    /// hosts never trigger it.
    /// </summary>
    public static string DbgEngDirectory => s_dbgEngDirectory.Value;

    /// <summary>Repo-built native SOS (<c>sos.dll</c>) from <see cref="RepoLayout.ArtifactsBinNative"/>.
    /// Windows-only (the dbgeng <c>.load</c> target); resolved lazily.</summary>
    public static string SosPath => s_sosPath.Value;

    /// <summary>Repo-built <c>dotnet-dump</c> managed entry point, run as <c>dotnet &lt;dll&gt;</c>.</summary>
    public static string DotNetDumpDll => s_dotNetDumpDll.Value;

    /// <summary>
    /// The native lldb plugin (<c>libsosplugin.so</c> on Linux, <c>libsosplugin.dylib</c> on macOS) that
    /// SOS loads into lldb via <c>plugin load</c>, taken from <see cref="RepoLayout.ArtifactsBinNative"/>.
    /// Non-Windows; resolved lazily.
    /// </summary>
    public static string LldbPluginPath => s_lldbPluginPath.Value;

    /// <summary>
    /// The <c>lldb</c> executable the harness drives. Resolution mirrors <c>eng/build.sh</c>: the
    /// <c>LLDB_PATH</c> env var first, then (on macOS) Xcode's lldb at
    /// <c>$(xcode-select -p)/usr/bin/lldb</c> (it carries the debugging entitlements), then a plain
    /// <c>lldb</c> on <c>PATH</c>. Non-Windows; resolved lazily.
    /// </summary>
    public static string LldbExe => s_lldbExe.Value;

    /// <summary>
    /// The .NET runtime directory SOS hosts its managed extension on (the <c>sethostruntime</c> target).
    /// Points at the repo's locally-acquired <c>.dotnet</c> shared runtime (highest net10 present), so the
    /// host runtime is deterministic and hermetic rather than auto-detected from <c>PATH</c>.
    /// </summary>
    public static string HostRuntimeDirectory => s_hostRuntimeDirectory.Value;

    /// <summary>
    /// Full path to the <c>createdump</c> executable the debugger host's hosted .NET runtime can use to
    /// write a crash dump. NativeAOT components (notably the universal cDAC) may not have a neighboring
    /// <c>createdump</c>, so the crash-dump environment points explicitly at the one from the host runtime.
    /// </summary>
    public static string CreateDumpPath => s_createDumpPath.Value;

    /// <summary>
    /// Directory containing the DAC (<c>mscordaccore.dll</c> / <c>libmscordaccore.so</c> /
    /// <c>libmscordaccore.dylib</c>) that matches the runtime a self-contained single-file debuggee of the
    /// given <paramref name="coreVersion"/> bundles. Self-contained single-file apps carry the runtime
    /// inside the exe, so a native debugger can't find the DAC next to a runtime on disk and (hermetically)
    /// can't download it. The cdb host loads it explicitly via <c>.cordll -lp</c>; the lldb host adds it as a
    /// local symbol-store directory via <c>setsymbolserver -directory</c>. The version is the runtime patch
    /// the single-file publish resolved against (from the install manifest and test runtime installation,
    /// or the matching runtime pack cache). Returns <c>null</c> if it can't be located.
    /// </summary>
    public static string? SingleFileDacDirectory(CoreVersion coreVersion) =>
        s_singleFileDacDirectory.GetOrAdd(coreVersion, ResolveSingleFileDacDirectory);

    /// <summary>
    /// Optional local directory containing an override universal cDAC
    /// (<c>libmscordaccore_universal.so</c> / platform equivalent) for cDAC test rows. Defaults to
    /// <c>artifacts/cdac-override/&lt;Configuration&gt;</c> and can be overridden with
    /// <c>SOSHARNESS_CDAC_DIR</c>.
    /// </summary>
    public static string? CDacOverrideDirectory => s_cdacOverrideDirectory.Value;

    /// <summary>
    /// Native SOS resolves the universal cDAC from the SOS module directory. Keep the lldb plugin output
    /// directory aligned with the configured override before cDAC rows load SOS.
    /// </summary>
    public static void EnsureLldbPluginCDacOverride()
    {
        string? overrideDirectory = CDacOverrideDirectory;
        if (overrideDirectory is null)
        {
            return;
        }

        string source = Path.Combine(overrideDirectory, CDacFileName);
        string destination = Path.Combine(Path.GetDirectoryName(LldbPluginPath)!, CDacFileName);
        lock (s_cdacCopyLock)
        {
            if (!File.Exists(destination) || !FilesEqual(source, destination))
            {
                File.Copy(source, destination, overwrite: true);
            }
        }
    }

    // Lazy so each host only resolves the tools it actually needs: the non-Windows lldb/dotnet-dump hosts
    // never touch the Windows-only dbgeng/sos.dll resolvers (which would throw for lack of those payloads),
    // and the Windows cdb host never touches the lldb resolvers.
    private static readonly Lazy<string> s_dbgEngDirectory = new(ResolveDbgEngDirectory);
    private static readonly Lazy<string> s_sosPath = new(ResolveSosPath);
    private static readonly Lazy<string> s_dotNetDumpDll = new(ResolveDotNetDumpDll);
    private static readonly Lazy<string> s_lldbPluginPath = new(ResolveLldbPluginPath);
    private static readonly Lazy<string> s_lldbExe = new(ResolveLldbExe);
    private static readonly Lazy<string> s_hostRuntimeDirectory = new(ResolveHostRuntimeDirectory);
    private static readonly Lazy<string> s_createDumpPath = new(ResolveCreateDumpPath);
    private static readonly Lazy<string?> s_cdacOverrideDirectory = new(ResolveCDacOverrideDirectory);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<CoreVersion, string?> s_singleFileDacDirectory = new();
    private static readonly object s_cdacCopyLock = new();

    private static string ResolveDbgEngDirectory()
    {
        string? configuredDirectory = Environment.GetEnvironmentVariable("SOSHARNESS_DBGENG_ROOT");
        if (!string.IsNullOrEmpty(configuredDirectory))
        {
            string directory = Path.GetFullPath(configuredDirectory);
            string dbgEngPath = Path.Combine(directory, "dbgeng.dll");
            if (File.Exists(dbgEngPath))
            {
                return directory;
            }

            throw new FileNotFoundException(
                $"Could not locate dbgeng.dll in the configured SOS harness DbgEng directory '{directory}'.",
                dbgEngPath);
        }

        string relativeNative = Path.Combine("runtimes", $"win-{RepoLayout.TargetArch}", "native");

        foreach (string root in NuGetPackageRoots())
        {
            string pkg = Path.Combine(root, "cdb-sos");
            if (!Directory.Exists(pkg))
            {
                continue;
            }

            // Prefer the highest version present.
            foreach (string versionDir in Directory.GetDirectories(pkg).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string native = Path.Combine(versionDir, relativeNative);
                if (File.Exists(Path.Combine(native, "dbgeng.dll")))
                {
                    return native;
                }
            }
        }

        throw new FileNotFoundException(
            "Could not locate dbgeng.dll from the cdb-sos package. Restore the harness test project so " +
            "its PackageDownload populates the NuGet cache.");
    }

    private static string ResolveSosPath()
    {
        string path = Path.Combine(RepoLayout.ArtifactsBinNative, "sos.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Repo-built native SOS not found at '{path}'. Build the repo (Build.cmd) so the native " +
                "SOS is produced for this configuration/architecture.", path);
        }

        return path;
    }

    private static string ResolveLldbPluginPath()
    {
        string name = OperatingSystem.IsMacOS() ? "libsosplugin.dylib" : "libsosplugin.so";
        string path = Path.Combine(RepoLayout.ArtifactsBinNative, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Repo-built lldb SOS plugin not found at '{path}'. Build the repo (./build.sh) so the native " +
                "lldb plugin is produced for this configuration/architecture.", path);
        }

        return path;
    }

    private static string ResolveLldbExe()
    {
        // 1) Explicit override (what eng/build.sh exports), if it points at a real file.
        string? env = Environment.GetEnvironmentVariable("LLDB_PATH");
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
        {
            return env;
        }

        // 2) macOS: Xcode's lldb is signed with the debugging entitlements needed to drive a process and
        //    to load core dumps, so prefer it over anything else.
        if (OperatingSystem.IsMacOS())
        {
            string? developerDir = TryRun("xcode-select", "-p");
            if (!string.IsNullOrWhiteSpace(developerDir))
            {
                string candidate = Path.Combine(developerDir.Trim(), "usr", "bin", "lldb");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // 3) A plain `lldb` on PATH.
        string? onPath = FindOnPath("lldb");
        if (onPath is not null)
        {
            return onPath;
        }

        throw new FileNotFoundException(
            "Could not locate an 'lldb' executable. Set LLDB_PATH, install lldb on PATH, or (on macOS) " +
            "install Xcode.");
    }

    private static string ResolveHostRuntimeDirectory()
    {
        // SOS hosts its managed extension on a .NET runtime; point it at the repo's locally-acquired
        // .dotnet shared runtime so it's deterministic. Any recent runtime works as a host (it need not
        // match the target's runtime), so pick the highest net10 present.
        string sharedRoot = Path.Combine(RepoLayout.DotNetRoot, "shared", "Microsoft.NETCore.App");
        if (Directory.Exists(sharedRoot))
        {
            string? best = Directory.GetDirectories(sharedRoot)
                .Select(Path.GetFileName)
                .Where(v => v is not null && v.StartsWith("10.0.", StringComparison.Ordinal))
                .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (best is not null)
            {
                return Path.Combine(sharedRoot, best);
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate a net10 host runtime under '{sharedRoot}'. Run ./build.sh so the repo's " +
            ".dotnet runtime is acquired.");
    }

    private static string ResolveCreateDumpPath()
    {
        string exe = OperatingSystem.IsWindows() ? "createdump.exe" : "createdump";
        string candidate = Path.Combine(HostRuntimeDirectory, exe);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException(
            $"createdump not found at '{candidate}'. Run ./build.sh so the repo's .dotnet runtime is acquired.", candidate);
    }

    private static string? FindOnPath(string fileName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? TryRun(string fileName, string arguments)
    {
        try
        {
            using System.Diagnostics.Process? p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null)
            {
                return null;
            }

            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveDotNetDumpDll()
    {
        // dotnet-dump targets net8.0 and is published under the repo artifacts; prefer the published
        // copy (self-contained closure) and fall back to the plain build output.
        string baseDir = Path.Combine(RepoLayout.ArtifactsBin, "dotnet-dump", RepoLayout.ArtifactsConfiguration, "net8.0");
        string published = Path.Combine(baseDir, "publish", "dotnet-dump.dll");
        if (File.Exists(published))
        {
            return published;
        }

        string built = Path.Combine(baseDir, "dotnet-dump.dll");
        if (File.Exists(built))
        {
            return built;
        }

        throw new FileNotFoundException(
            $"Repo-built dotnet-dump not found under '{baseDir}'. Build the repo (Build.cmd) so dotnet-dump " +
            "is produced.", published);
    }

    private static string? ResolveSingleFileDacDirectory(CoreVersion coreVersion)
    {
        string rid = RepoLayout.Rid; // win-x64 / linux-x64 / osx-arm64 / ...
        string packId = $"microsoft.netcore.app.runtime.{rid}";
        string relativeNative = Path.Combine("runtimes", rid, "native");
        string dacFileName = DacFileName; // mscordaccore.dll / libmscordaccore.so / libmscordaccore.dylib
        int major = CoreVersions.Major(coreVersion);

        // Preferred: the exact runtime version the single-file publish resolved against (what the install
        // manifest recorded for this framework), read straight from the test runtime installation. The
        // corresponding runtime pack is not necessarily restored into the user's NuGet cache.
        string? pinned = CoreVersions.RuntimeVersion(coreVersion);
        if (!string.IsNullOrEmpty(pinned))
        {
            string sharedRuntime = Path.Combine(
                RepoLayout.DotnetTestRoot,
                "shared",
                "Microsoft.NETCore.App",
                pinned!);
            if (File.Exists(Path.Combine(sharedRuntime, dacFileName)))
            {
                return sharedRuntime;
            }

            foreach (string root in NuGetPackageRoots())
            {
                string native = Path.Combine(root, packId, pinned!, relativeNative);
                if (File.Exists(Path.Combine(native, dacFileName)))
                {
                    return native;
                }
            }
        }

        // Fallback: the highest patch of this major present in the runtime-pack cache.
        string majorPrefix = $"{major}.0.";
        foreach (string root in NuGetPackageRoots())
        {
            string pkg = Path.Combine(root, packId);
            if (!Directory.Exists(pkg))
            {
                continue;
            }

            string? best = Directory.GetDirectories(pkg)
                .Select(Path.GetFileName)
                .Where(v => v is not null && v.StartsWith(majorPrefix, StringComparison.Ordinal))
                .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (best is not null)
            {
                string native = Path.Combine(pkg, best, relativeNative);
                if (File.Exists(Path.Combine(native, dacFileName)))
                {
                    return native;
                }
            }
        }

        return null;
    }

    private static string? ResolveCDacOverrideDirectory()
    {
        string? env = Environment.GetEnvironmentVariable("SOSHARNESS_CDAC_DIR");
        string directory = string.IsNullOrEmpty(env)
            ? Path.Combine(RepoLayout.Root, "artifacts", "cdac-override", RepoLayout.ArtifactsConfiguration)
            : env;

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

    /// <summary>The platform-specific DAC module file name: <c>mscordaccore.dll</c> on Windows,
    /// <c>libmscordaccore.dylib</c> on macOS, <c>libmscordaccore.so</c> elsewhere.</summary>
    private static string DacFileName =>
        OperatingSystem.IsWindows() ? "mscordaccore.dll" :
        OperatingSystem.IsMacOS() ? "libmscordaccore.dylib" : "libmscordaccore.so";

    private static string CDacFileName =>
        OperatingSystem.IsWindows() ? "mscordaccore_universal.dll" :
        OperatingSystem.IsMacOS() ? "libmscordaccore_universal.dylib" : "libmscordaccore_universal.so";

    private static IEnumerable<string> NuGetPackageRoots()
    {
        string? env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env))
        {
            yield return env;
        }

        yield return Path.Combine(UserProfile, ".nuget", "packages");
    }

    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
