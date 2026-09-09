// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.InteropServices;

namespace SOS.TestHarness;

/// <summary>
/// Locates the diagnostics repo root and the well-known build output locations the harness
/// consumes (repo-built native SOS, repo-built dotnet-dump, the pre-built debuggees, and the
/// scratch dump directory). The root is found by walking up from the test output directory and
/// looking for either the repository markers or a staged-payload marker, so the harness works
/// regardless of where the test assembly is run from.
/// </summary>
public static class RepoLayout
{
    private const string PayloadMarker = ".sos-test-payload";

    /// <summary>The build configuration of the repo-built tools (native SOS, dotnet-dump), embedded by
    /// MSBuild in the harness assembly.</summary>
    public static string ArtifactsConfiguration { get; } =
        typeof(RepoLayout).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "SOS.TestHarness.Configuration")
            .Value!;

    /// <summary>The target framework of the running harness, such as <c>net10.0</c>.</summary>
    public static string TestTargetFramework { get; } =
        typeof(RepoLayout).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "SOS.TestHarness.TargetFramework")
            .Value!;

    /// <summary>The repository or staged payload root.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Whether the harness is running from a self-contained staged payload.</summary>
    public static bool IsPayload { get; } = File.Exists(Path.Combine(Root, PayloadMarker));

    /// <summary>Whether the harness is executing as a Helix work item.</summary>
    public static bool IsHelix { get; } =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HELIX_WORKITEM_ROOT"));

    /// <summary>Writable root for payload overlays and transient harness state.</summary>
    public static string WorkRoot { get; } =
        Environment.GetEnvironmentVariable("HELIX_WORKITEM_ROOT") is { Length: > 0 } root
            ? Path.GetFullPath(root)
            : Path.Combine(Root, "artifacts", "tmp", "sos-harness", ArtifactsConfiguration);

    /// <summary><c>artifacts/bin</c> under the repo root.</summary>
    public static string ArtifactsBin => Path.Combine(Root, "artifacts", "bin");

    /// <summary>The native build output directory, using a prepared writable payload overlay when present.</summary>
    public static string ArtifactsBinNative =>
        PreferPreparedDirectory(
            Path.Combine(WorkRoot, ".sos-harness", "native"),
            Path.Combine(ArtifactsBin, $"{TargetOS}.{TargetArch}.{ArtifactsConfiguration}"));

    /// <summary>The processor architecture token used in repo artifact paths (<c>x64</c>/<c>x86</c>/<c>arm64</c>).</summary>
    public static string TargetArch { get; } = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        _ => "x64",
    };

    /// <summary>The OS token used in repo native artifact paths. The native build lowercases the OS token
    /// for non-Windows (e.g. <c>linux.x64.Debug</c>, <c>osx.arm64.Debug</c>) but keeps <c>Windows_NT</c> on
    /// Windows, so match that casing here or the native output directory won't be found.</summary>
    public static string TargetOS { get; } =
        OperatingSystem.IsWindows() ? "Windows_NT" :
        OperatingSystem.IsMacOS() ? "osx" : "linux";

    /// <summary>The runtime identifier of the current test leg (e.g. <c>win-x64</c> or
    /// <c>linux-musl-x64</c>), embedded by the build so artifact lookup preserves RID distinctions that
    /// cannot be inferred from <see cref="OperatingSystem"/>.</summary>
    public static string Rid { get; } =
        typeof(RepoLayout).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "SOS.TestHarness.TargetRid")
            .Value!;

    /// <summary>The .NET root used by harness subprocesses. Staged payloads use their multi-runtime install;
    /// repository runs use the locally acquired build SDK.</summary>
    public static string DotNetRoot =>
        IsPayload ? DotnetTestRoot : Path.Combine(Root, ".dotnet");

    public static string DotNetExe => Path.Combine(DotNetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    /// <summary>The platform suffix for an apphost executable: <c>.exe</c> on Windows, none elsewhere
    /// (Linux/macOS apphosts have no extension).</summary>
    public static string ExeSuffix => OperatingSystem.IsWindows() ? ".exe" : string.Empty;

    /// <summary>Path to a debuggee project under the SOS.UnitTests Debuggees tree.</summary>
    public static string DebuggeeProject(string name) =>
        Path.Combine(Root, "src", "tests", "SOS.UnitTests", "Debuggees", name, name + ".csproj");

    /// <summary>The pre-built Core output directory for a debuggee and target framework (e.g.
    /// <c>net8.0</c>/<c>net11.0</c>), as produced by Debuggees.proj.</summary>
    public static string CoreDebuggeeDir(string name, string tfm) =>
        Path.Combine(ArtifactsBin, name, ArtifactsConfiguration, tfm);

    /// <summary>The build-produced self-contained single-file publish directory for a debuggee.</summary>
    public static string SingleFileDebuggeeDir(string name, string tfm) =>
        Path.Combine(ArtifactsBin, name, ArtifactsConfiguration, tfm, Rid, "publish");

    /// <summary>The pre-built desktop .NET Framework output directory for a debuggee.</summary>
    public static string FrameworkDebuggeeDir(string name) =>
        Path.Combine(ArtifactsBin, name, ArtifactsConfiguration, "net462");

    /// <summary>The multi-version test runtime root, using a prepared executable overlay when present.
    /// <c>eng/InstallRuntimes.proj</c> populates it with every tested runtime.</summary>
    public static string DotnetTestRoot =>
        PreferPreparedDirectory(
            Path.Combine(WorkRoot, ".sos-harness", "dotnet-test"),
            Path.Combine(Root, "artifacts", "dotnet-test"));

    /// <summary>The multi-version test .NET host (<c>artifacts/dotnet-test/dotnet[.exe]</c>). This is the
    /// net11-capable SDK that <c>Debuggees.proj</c> uses to pre-build the debuggees, so local Core fallback
    /// builds must use it too — the repo's <c>.dotnet</c> build SDK (e.g. 10.0.x) refuses to target newer
    /// frameworks (<c>NETSDK1045</c>).</summary>
    public static string DotnetTestExe => Path.Combine(DotnetTestRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    /// <summary>Scratch directory for harness-produced artifacts.</summary>
    public static string Scratch { get; } =
        IsHelix ? Path.Combine(WorkRoot, ".sos-harness", "scratch") : WorkRoot;

    /// <summary>Writable executable overlay used by staged payloads.</summary>
    public static string? ExecutableRoot { get; } =
        IsPayload ? Path.Combine(WorkRoot, ".sos-harness", "executables") : null;

    /// <summary>Directory containing the cdb-sos payload.</summary>
    public static string CdbRoot { get; } = Path.Combine(Root, "artifacts", "cdb-sos");

    /// <summary>Root where Helix result artifacts should be written, with a repository fallback.</summary>
    public static string UploadRoot { get; } =
        Environment.GetEnvironmentVariable("HELIX_WORKITEM_UPLOAD_ROOT") is { Length: > 0 } root
            ? Path.GetFullPath(root)
            : Path.Combine(Root, "artifacts", "TestResults", "SOS.Tests");

    public static string CrashDumpDirectory { get; } =
        IsHelix
            ? Path.Combine(UploadRoot, "failure-diagnostics", "crashdumps")
            : Path.Combine(Root, "artifacts", "replays", "crashdumps");

    public static string ReplayDirectory { get; } =
        IsHelix ? Path.Combine(UploadRoot, "SOS-replays") : UploadRoot;

    public static string? LldbTraceFile { get; } =
        IsHelix ? Path.Combine(UploadRoot, $"SOS.Tests-{Rid}-{ArtifactsConfiguration}.lldb.log") : null;

    /// <summary>
    /// A hermetic, local-only symbol path for the SOS host child processes. The dev machine's
    /// <c>_NT_SYMBOL_PATH</c> often points at the Azure-authed <c>symweb</c> server, which makes SOS's
    /// host init pull in Azure.Identity (and fail loading its closure) and would make tests depend on
    /// the network. We point the children at a local cache only — debuggee PDBs are found next to the
    /// module, so managed source/line resolution still works.
    /// </summary>
    public static string SymbolCache { get; } = Path.Combine(Scratch, "symcache");

    private static string PreferPreparedDirectory(string preparedPath, string defaultPath) =>
        Directory.Exists(preparedPath) ? preparedPath : defaultPath;

    private static string FindRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, PayloadMarker)) ||
                (File.Exists(Path.Combine(dir, "global.json")) &&
                 File.Exists(Path.Combine(dir, "Build.cmd"))))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the diagnostics repo root or {PayloadMarker} by walking up from " +
            AppContext.BaseDirectory);
    }
}
