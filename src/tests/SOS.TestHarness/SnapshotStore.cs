// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace SOS.TestHarness;

/// <summary>
/// Locates and memoizes the runnable debuggee and produces the dump for each <c>(flavor, target, coreVersion,
/// stopPoint)</c>. Each <c>(flavor, target, coreVersion)</c> is acquired once and captured once into its
/// own dump directory — so no two tests ever write the same artifact. The DAC axis is not a
/// capture dimension: legacy and cDAC reuse the same dump (only <c>runtimes --usecdac</c> differs at debug
/// time), so it never appears in these keys.
///
/// Debuggee acquisition follows the repo's build model:
/// <list type="bullet">
///   <item><b>Core (net8.0–net11.0)</b> is the pre-built debuggee produced by the repo build
///   (<c>Debuggees.proj</c>) under <c>artifacts/bin/&lt;Name&gt;/&lt;Config&gt;/net{N}.0</c>; the harness
///   consumes it directly (and builds the single project on demand for local development if it isn't there
///   yet), launching it against the multi-version test runtime install so its apphost binds the matching
///   runtime.</item>
///   <item><b>SingleFile</b> is pre-published by <c>Debuggees.proj</c> once per tested runtime, RID, and
///   configuration. Tests only locate and consume that immutable output.</item>
///   <item><b>Framework (net462)</b> is pre-built on Windows by <c>Debuggees.proj</c>; local development
///   falls back to an on-demand build when that output is absent.</item>
/// </list>
///
/// Capture mechanism depends on the flavor and stop kind:
/// <list type="bullet">
///   <item><b>Snapshot stops (Core / SingleFile)</b> self-snapshot mid-run from inside the debuggee via
///   the repo-built <c>dotnet-dump collect</c>.</item>
///   <item><b>Crash stop, Core</b> lets the runtime's createdump write the dump.</item>
///   <item><b>Crash stop, SingleFile on Windows</b> can't use createdump, so it's captured with dbgeng like desktop.</item>
///   <item><b>Framework</b> (desktop) is always captured externally by <see cref="DbgEngCapturer"/>.</item>
/// </list>
/// </summary>
public static class SnapshotStore
{
    private static readonly TimeSpan s_captureTimeout = TimeSpan.FromMinutes(5);

    // One acquisition per (flavor, target, coreVersion); thread-safe via Lazy.
    private static readonly ConcurrentDictionary<(Flavor Flavor, string Target, CoreVersion CoreVersion), Lazy<string>> s_targetExe = new();

    // One capture per (flavor, target, gcType, dumpKind, coreVersion) (distinct dump dirs); thread-safe via
    // Lazy. The DAC axis is deliberately absent: the same dump is reused for both legacy and cDAC (only
    // `runtimes --usecdac` differs at debug time), so capture must not be keyed on it.
    private static readonly ConcurrentDictionary<(Flavor Flavor, string Target, GcType GcType, DumpKind DumpKind, CoreVersion CoreVersion), Lazy<string>> s_captured = new();

    // The out-of-process desktop capturer, located/built once.
    private static readonly Lazy<string> s_capturerDll = new(() => SubprocessDll("SOS.TestHarness.Capturer"));

    private static string CapturerDll => s_capturerDll.Value;

    // The out-of-process dbgeng engine host, located/built once.
    private static readonly Lazy<string> s_engineHostDll = new(() => SubprocessDll("SOS.TestHarness.EngineHost"));
    private static readonly object s_subprocessBuildLock = new();

    /// <summary>Path to the built EngineHost.dll (the subprocess dbgeng backend), produced on first use.</summary>
    public static string EngineHostDll => s_engineHostDll.Value;

    /// <summary>Path to the dump for one stop point of a target in a flavor/GC/dump-kind/version, producing it on first use.</summary>
    public static string GetDump(Flavor flavor, string targetName, string stopName, GcType gcType = GcType.Workstation, DumpKind dumpKind = DumpKind.Heap, CoreVersion coreVersion = CoreVersion.Net10)
    {
        TargetDefinition target = TargetCatalog.Get(targetName);
        target.Stop(stopName); // validate

        string dumpDir = s_captured
            .GetOrAdd((flavor, targetName, gcType, dumpKind, coreVersion), key => new Lazy<string>(() => CaptureTarget(key.Flavor, TargetCatalog.Get(key.Target), key.GcType, key.DumpKind, key.CoreVersion)))
            .Value;

        string dump = Path.Combine(dumpDir, stopName + ".dmp");
        if (!File.Exists(dump))
        {
            throw new InvalidOperationException(
                $"Capture of {flavor}/{gcType}/{dumpKind}/{CoreVersions.Tfm(coreVersion)}/{targetName} did not produce a dump for stop '{stopName}' at '{dump}'.");
        }

        return dump;
    }

    /// <summary>Path to the runnable executable for a target in a flavor/version, producing it on first use.</summary>
    public static string TargetExe(Flavor flavor, string targetName, CoreVersion coreVersion = CoreVersion.Net10)
    {
        string exe = s_targetExe
            .GetOrAdd((flavor, targetName, coreVersion), k => new Lazy<string>(() => AcquireTarget(k.Flavor, TargetCatalog.Get(k.Target), k.CoreVersion)))
            .Value;
        return EnsureExecutable(
            exe,
            Environment.GetEnvironmentVariable("SOSHARNESS_EXECUTABLE_ROOT"),
            RepoLayout.Root);
    }

    internal static string EnsureExecutable(string path, string? executableRoot, string repoRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            return path;
        }

        UnixFileMode mode = File.GetUnixFileMode(path);
        UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        if ((mode & execute) != execute)
        {
            if (string.IsNullOrEmpty(executableRoot))
            {
                File.SetUnixFileMode(path, mode | execute);
                return path;
            }

            string relative = Path.GetRelativePath(repoRoot, path);
            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Executable '{path}' is outside the repo root '{repoRoot}' and cannot be copied to the writable executable overlay.");
            }

            string destination = Path.Combine(executableRoot, relative);
            string sourceDirectory = Path.GetDirectoryName(path)!;
            string destinationDirectory = Path.GetDirectoryName(destination)!;

            lock (BuildLockFor(destination))
            {
                if (File.Exists(destination) &&
                    (File.GetUnixFileMode(destination) & execute) == execute)
                {
                    return destination;
                }

                Directory.CreateDirectory(destinationDirectory);
                foreach (string sibling in Directory.EnumerateFiles(sourceDirectory))
                {
                    string siblingDestination = Path.Combine(destinationDirectory, Path.GetFileName(sibling));
                    if (string.Equals(sibling, path, StringComparison.Ordinal))
                    {
                        File.Copy(sibling, siblingDestination, overwrite: true);
                    }
                    else if (!File.Exists(siblingDestination))
                    {
                        File.CreateSymbolicLink(siblingDestination, sibling);
                    }
                }

                UnixFileMode destinationMode = File.GetUnixFileMode(destination);
                File.SetUnixFileMode(destination, destinationMode | execute);
            }

            return destination;
        }

        return path;
    }

    private static string DumpDir(Flavor flavor, string target, GcType gcType, DumpKind dumpKind, CoreVersion coreVersion) =>
        Path.Combine(RepoLayout.Scratch, "dumps", flavor.ToString().ToLowerInvariant(),
            gcType.ToString().ToLowerInvariant(), dumpKind.ToString().ToLowerInvariant(),
            CoreVersions.Tfm(coreVersion), target);

    private static string CaptureTarget(Flavor flavor, TargetDefinition target, GcType gcType, DumpKind dumpKind, CoreVersion coreVersion)
    {
        string dumpDir = DumpDir(flavor, target.Name, gcType, dumpKind, coreVersion);
        Directory.CreateDirectory(dumpDir);

        // Resolve (build if needed) the debuggee first, then reuse the cached dumps only if they were
        // captured from THIS exe (i.e. are at least as new as it). A rebuilt exe has a fresh PDB whose
        // GUID won't match an older dump, so a stale dump must be re-captured.
        string exe = TargetExe(flavor, target.Name, coreVersion);
        DateTime exeTime = File.GetLastWriteTimeUtc(exe);
        if (target.StopPoints.All(s => IsUpToDate(Path.Combine(dumpDir, s.Name + ".dmp"), exeTime)))
        {
            return dumpDir;
        }

        // On Windows, a reduced Core dump of an unsigned runtime needs a machine-wide dbghelp setting.
        // Use a Full dump when that setting is absent so normal test runs remain self-contained.
        DumpKind captureKind = DumpGenerationRequirements.ResolveCaptureKind(flavor, dumpKind);

        bool isCrash = target.StopPoints.Any(s => s.Kind == StopKind.Crash);

        if (flavor == Flavor.Framework)
        {
            // Desktop: no diagnostics IPC; dbgeng captures both snapshot (bpmd) and crash (second-chance).
            // Run it out-of-process so a dbgeng crash dies with the child, not the test host.
            CaptureWithDbgEng(TargetExe(flavor, target.Name, coreVersion), target, dumpDir, gcType, captureKind);
        }
        else if (isCrash && flavor == Flavor.SingleFile && OperatingSystem.IsWindows())
        {
            // Self-contained single-file on Windows doesn't ship/launch createdump, so capture its crash
            // with dbgeng like desktop (also out-of-process). On Linux/macOS the bundled runtime's
            // createdump handles single-file crashes, so we fall through to CaptureCrashViaCreatedump.
            CaptureWithDbgEng(TargetExe(flavor, target.Name, coreVersion), target, dumpDir, gcType, captureKind);
        }
        else if (isCrash)
        {
            // .NET Core crash: let the runtime's createdump write the dump.
            CaptureCrashViaCreatedump(flavor, target, dumpDir, gcType, captureKind, coreVersion);
        }
        else
        {
            // Snapshot stops on Core / SingleFile: self-snapshot mid-run via markers.
            SelfCollectCapture(flavor, target, dumpDir, gcType, captureKind, coreVersion);
        }

        return dumpDir;
    }

    /// <summary>Run the Capturer child to produce dumps via in-process dbgeng (desktop, or single-file crash).</summary>
    private static void CaptureWithDbgEng(string exePath, TargetDefinition target, string dumpDir, GcType gcType, DumpKind dumpKind)
    {
        ProcessStartInfo psi = new(RepoLayout.DotNetExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(CapturerDll);
        psi.ArgumentList.Add(exePath);
        psi.ArgumentList.Add(target.Name);
        psi.ArgumentList.Add(dumpDir);
        psi.ArgumentList.Add(dumpKind.ToString());

        // Hermetic, local-only symbols: the Capturer hosts dbgeng+SOS, and the dev's _NT_SYMBOL_PATH may
        // point at the Azure-authed symweb, which crashes SOS host init (loading Azure.Identity's closure).
        Directory.CreateDirectory(RepoLayout.SymbolCache);
        psi.Environment["_NT_SYMBOL_PATH"] = RepoLayout.SymbolCache;
        ApplyGcType(psi, gcType);

        using Process p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Capturer");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"Capturer failed for {target.Name} ({p.ExitCode}):\n{stdout}\n{stderr}");
        }
    }

    /// <summary>
    /// Core/SingleFile crash capture: launch the target with the runtime's crash-dump env vars set and let
    /// it crash. The runtime's createdump writes the dump to the crash stop's path; the process exits
    /// non-zero (it crashed), so we
    /// verify the dump exists rather than the exit code.
    /// </summary>
    private static void CaptureCrashViaCreatedump(Flavor flavor, TargetDefinition target, string dumpDir, GcType gcType, DumpKind dumpKind, CoreVersion coreVersion)
    {
        string exe = TargetExe(flavor, target.Name, coreVersion);
        StopPoint crash = target.StopPoints.Single(s => s.Kind == StopKind.Crash);
        string dumpPath = Path.Combine(dumpDir, crash.Name + ".dmp");

        ProcessStartInfo psi = new(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["DOTNET_DbgEnableMiniDump"] = "1";
        psi.Environment["DOTNET_DbgMiniDumpType"] = CreatedumpType(flavor, dumpKind);
        psi.Environment["DOTNET_DbgMiniDumpName"] = dumpPath;
        psi.Environment["DOTNET_CreateDumpDiagnostics"] = "1";
        ApplyRuntimeRoot(psi, flavor);
        ApplyMacOsDumpConfig(psi);
        ApplyGcType(psi, gcType);

        // Windows createdump can outlive the crashing target while retaining its redirected handles.
        BoundedProcessResult result = BoundedProcess.Run(
            psi,
            s_captureTimeout,
            isolateLinuxProcessGroup: true,
            outputDrainTimeout: s_captureTimeout);

        if (!File.Exists(dumpPath))
        {
            throw new InvalidOperationException(
                $"createdump did not produce '{dumpPath}' for {target.Project} ({flavor}); exit {result.ExitCode}.\n" +
                $"stdout:\n{result.StandardOutput}\n" +
                $"stderr:\n{result.StandardError}");
        }
    }

    /// <summary>Core/SingleFile snapshot capture: run the target once; its markers self-snapshot mid-run.</summary>
    /// <summary>
    /// macOS-only dump configuration applied to every debuggee we capture a dump from (createdump on crash,
    /// or the in-process <c>dotnet-dump collect</c> self-snapshot). createdump on macOS defaults to a Mach-O
    /// core, which SOS/ClrMD cannot read; <c>DOTNET_DbgEnableElfDumpOnMacOS=1</c> makes it emit an ELF core
    /// instead (matching the legacy harness). The diagnostic-IPC socket the runtime opens lives under
    /// <c>$TMPDIR</c>, and macOS's default <c>$TMPDIR</c> (<c>/var/folders/…</c>) routinely blows past the
    /// 104-byte Unix-domain-socket path limit, so point the debuggee at a short <c>TMPDIR</c>. No-op off macOS.
    /// </summary>
    private static void ApplyMacOsDumpConfig(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        psi.Environment["DOTNET_DbgEnableElfDumpOnMacOS"] = "1";
        psi.Environment["TMPDIR"] = "/tmp";
    }

    /// <summary>
    /// Point a framework-dependent (Core) debuggee at the multi-version test runtime install so its apphost
    /// resolves the runtime matching its target framework (e.g. a net8 debuggee binds 8.0.x, net11 binds the
    /// installed preview). Self-contained single-file and desktop Framework debuggees carry / don't use a
    /// shared runtime, so this is a no-op for them. <c>DOTNET_MULTILEVEL_LOOKUP=0</c> keeps resolution
    /// strictly within the test install (no machine-wide fallback), so the dump's coreclr — and therefore the
    /// DAC SOS later loads — is the deterministic, on-disk one.
    /// </summary>
    private static void ApplyRuntimeRoot(ProcessStartInfo psi, Flavor flavor)
    {
        if (flavor != Flavor.Core)
        {
            return;
        }

        psi.Environment["DOTNET_ROOT"] = RepoLayout.DotnetTestRoot;
        psi.Environment["DOTNET_ROOT(x86)"] = RepoLayout.DotnetTestRoot;
        psi.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
    }

    /// <summary>Apply the GC-mode env vars to a debuggee launch. Server forces a deterministic multi-heap
    /// GC (a fixed heap count with DATAS off, so it can't collapse back to a single heap).</summary>
    private static void ApplyGcType(ProcessStartInfo psi, GcType gcType)
    {
        if (gcType == GcType.Server)
        {
            psi.Environment["DOTNET_gcServer"] = "1";
            psi.Environment["DOTNET_GCHeapCount"] = "4";
            psi.Environment["DOTNET_GCDynamicAdaptationMode"] = "0";
        }
    }

    /// <summary>
    /// The <c>createdump</c>/<c>DOTNET_DbgMiniDumpType</c> value for a dump kind: Full=4, Heap=2, Mini=1,
    /// except single-file crash dumps which must use Full=4.
    /// </summary>
    private static string CreatedumpType(Flavor flavor, DumpKind dumpKind)
    {
        if (dumpKind == DumpKind.Full || flavor == Flavor.SingleFile)
        {
            // Single-file crash dumps cannot use createdump's reduced dump modes: Heap/Mini require DAC
            // region enumeration, but the single-file app does not have a loadable DAC beside it. Keep the
            // test matrix's Heap row, but capture it with the only supported createdump mode.
            return "4";
        }

        return dumpKind == DumpKind.Mini ? "1" : "2";
    }

    /// <summary>
    /// The <c>dotnet-dump collect --type</c> value for a dump kind. Single-file self-snapshots use Full
    /// because reduced dumps require the same unsupported DAC region enumeration as single-file crashes.
    /// </summary>
    private static string CollectType(Flavor flavor, DumpKind dumpKind) =>
        dumpKind == DumpKind.Full || flavor == Flavor.SingleFile ? "Full" : dumpKind.ToString();

    private static void SelfCollectCapture(Flavor flavor, TargetDefinition target, string dumpDir, GcType gcType, DumpKind dumpKind, CoreVersion coreVersion)
    {
        string exe = TargetExe(flavor, target.Name, coreVersion);
        ProcessStartInfo psi = new(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["SOSHARNESS_CAPTURE_DIR"] = dumpDir;
        // Tell the debuggee's stop-point helper which dotnet-dump to self-collect with (the repo-built one).
        psi.Environment["SOSHARNESS_DOTNET"] = RepoLayout.DotNetExe;
        psi.Environment["SOSHARNESS_DOTNETDUMP_DLL"] = ToolPaths.DotNetDumpDll;
        psi.Environment["SOSHARNESS_DUMP_TYPE"] = CollectType(flavor, dumpKind);
        ApplyRuntimeRoot(psi, flavor);
        ApplyMacOsDumpConfig(psi);
        ApplyGcType(psi, gcType);

        BoundedProcessResult result = BoundedProcess.Run(
            psi,
            s_captureTimeout,
            isolateLinuxProcessGroup: true);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Target '{target.Project}' ({flavor}) failed ({result.ExitCode}):\n" +
                $"stdout:\n{result.StandardOutput}\n" +
                $"stderr:\n{result.StandardError}");
        }
    }

    /// <summary>
    /// Resolve the runnable debuggee for a flavor. All flavors prefer repo build outputs; Framework falls
    /// back to an on-demand build from the repo debuggee csproj for local development.
    /// </summary>
    private static string AcquireTarget(Flavor flavor, TargetDefinition target, CoreVersion coreVersion) => flavor switch
    {
        Flavor.Core => AcquireCore(target, coreVersion),
        Flavor.Framework => BuildFramework(target),
        Flavor.SingleFile => AcquireSingleFile(target, coreVersion),
        _ => throw new ArgumentOutOfRangeException(nameof(flavor)),
    };

    /// <summary>Consume the repo-built Core debuggee for the requested version; build the single project on
    /// demand if it's absent or older than the debuggee source (so a local debuggee edit is picked up).</summary>
    private static string AcquireCore(TargetDefinition target, CoreVersion coreVersion)
    {
        string tfm = CoreVersions.Tfm(coreVersion);
        string exe = Path.Combine(RepoLayout.CoreDebuggeeDir(target.Project, tfm), target.Project + RepoLayout.ExeSuffix);
        if (UsePrebuiltTargets)
        {
            if (File.Exists(exe))
            {
                return exe;
            }

            throw new FileNotFoundException(
                $"Pre-built Core debuggee '{target.Project}' ({tfm}) was not found at '{exe}'.",
                exe);
        }

        string project = RepoLayout.DebuggeeProject(target.Project);
        if (IsUpToDate(exe, NewestSourceWriteTime(project)))
        {
            return exe;
        }

        // Missing or stale relative to source — build just this debuggee for the requested framework (lands
        // at the same conventional artifacts path). Lock per project (not per framework): different TFMs of
        // one csproj share its obj/ and project.assets.json, so concurrent restores would corrupt each other.
        lock (BuildLockFor(project))
        {
            if (!IsUpToDate(exe, NewestSourceWriteTime(project)))
            {
                RunToCompletion(RepoLayout.DotnetTestExe,
                    $"build \"{project}\" -p:BuildProjectFramework={tfm} -c {RepoLayout.ArtifactsConfiguration}");
            }
        }

        if (!File.Exists(exe))
        {
            throw new InvalidOperationException($"Core build of {target.Project} ({tfm}) did not produce '{exe}'.");
        }

        return exe;
    }

    /// <summary>Consume the self-contained single-file debuggee published by the repo build.</summary>
    private static string AcquireSingleFile(TargetDefinition target, CoreVersion coreVersion)
    {
        string tfm = CoreVersions.Tfm(coreVersion);
        string outputDir = RepoLayout.SingleFileDebuggeeDir(target.Project, tfm);
        string exe = Path.Combine(outputDir, target.Project + RepoLayout.ExeSuffix);
        string runtimeVersionFile = Path.Combine(outputDir, "runtime.version");
        string? expectedRuntimeVersion = CoreVersions.RuntimeVersion(coreVersion);
        string? actualRuntimeVersion = File.Exists(runtimeVersionFile)
            ? File.ReadAllText(runtimeVersionFile).Trim()
            : null;

        if (!File.Exists(exe))
        {
            throw new FileNotFoundException(
                $"Pre-published single-file debuggee '{target.Project}' ({tfm}/{RepoLayout.Rid}) was not found at '{exe}'. " +
                "Build src/tests/Debuggees.proj before running SOS tests.",
                exe);
        }

        if (expectedRuntimeVersion is null ||
            !string.Equals(actualRuntimeVersion, expectedRuntimeVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Pre-published single-file debuggee '{target.Project}' ({tfm}/{RepoLayout.Rid}) has runtime version " +
                $"'{actualRuntimeVersion ?? "<missing>"}', but the installed test runtime manifest requires " +
                $"'{expectedRuntimeVersion ?? "<missing>"}'.");
        }

        return exe;
    }

    /// <summary>Build the Framework debuggee into the scratch tree, reusing the cached exe when it is newer
    /// than the debuggee source.</summary>
    private static string BuildFramework(TargetDefinition target)
    {
        string prebuilt = Path.Combine(RepoLayout.FrameworkDebuggeeDir(target.Project), target.Project + RepoLayout.ExeSuffix);
        if (File.Exists(prebuilt))
        {
            return prebuilt;
        }

        if (UsePrebuiltTargets)
        {
            throw new FileNotFoundException(
                $"Pre-built Framework debuggee '{target.Project}' was not found at '{prebuilt}'.",
                prebuilt);
        }

        string project = RepoLayout.DebuggeeProject(target.Project);
        string outDir = Path.Combine(RepoLayout.Scratch, "targets", "framework", target.Name);
        string exe = Path.Combine(outDir, target.Project + RepoLayout.ExeSuffix);
        DateTime sourceWriteTime = NewestSourceWriteTime(project);

        if (IsUpToDate(exe, sourceWriteTime))
        {
            return exe;
        }

        string config = RepoLayout.ArtifactsConfiguration;
        // Desktop SOS resolves source lines from a classic Windows PDB (read via DIA), not a
        // portable/embedded one — the repo's global props default DebugType to embedded, so force
        // a full (Windows) PDB next to the exe for the source-line tests.
        string args =
            $"build \"{project}\" -p:BuildProjectFramework=net462 -p:DebugType=full -p:DebugSymbols=true -c {config} -o \"{outDir}\"";

        // Rebuild only when stale (above). Different frameworks of one csproj share its obj/ (and
        // project.assets.json), so serialize fallback builds per project.
        lock (BuildLockFor(project))
        {
            if (!IsUpToDate(exe, sourceWriteTime))
            {
                RunToCompletion(RepoLayout.DotnetTestExe, args);
            }
        }

        if (!File.Exists(exe))
        {
            throw new InvalidOperationException($"Framework build of {target.Project} did not produce '{exe}'.");
        }

        return exe;
    }

    private static bool UsePrebuiltTargets =>
        string.Equals(
            Environment.GetEnvironmentVariable("SOSHARNESS_USE_PREBUILT_TARGETS"),
            "1",
            StringComparison.Ordinal);

    private static readonly ConcurrentDictionary<string, object> s_projectBuildLocks = new(StringComparer.OrdinalIgnoreCase);

    private static object BuildLockFor(string projectPath) =>
        s_projectBuildLocks.GetOrAdd(projectPath, _ => new object());

    /// <summary>Newest write time of the debuggee's sources (its <c>.cs</c> files + csproj), so a build is
    /// re-run only when the source actually changed (an unchanged build keeps a stable exe/PDB, which keeps
    /// the cached dumps — captured against that exe's PDB — valid).</summary>
    private static DateTime NewestSourceWriteTime(string projectFile)
    {
        string dir = Path.GetDirectoryName(projectFile)!;
        DateTime newest = File.GetLastWriteTimeUtc(projectFile);
        foreach (string cs in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            DateTime t = File.GetLastWriteTimeUtc(cs);
            if (t > newest)
            {
                newest = t;
            }
        }

        return newest;
    }

    /// <summary>True if <paramref name="output"/> exists and is at least as new as <paramref name="inputUtc"/>.</summary>
    private static bool IsUpToDate(string output, DateTime inputUtc) =>
        File.Exists(output) && File.GetLastWriteTimeUtc(output) >= inputUtc;

    /// <summary>
    /// Locate a subprocess host (EngineHost / Capturer), building it on demand for local development when
    /// the normal repository build has not produced it.
    /// </summary>
    private static string SubprocessDll(string name)
    {
        string dll = Path.Combine(RepoLayout.ArtifactsBin, name, RepoLayout.ArtifactsConfiguration, RepoLayout.TestTargetFramework, RepoLayout.Rid, name + ".dll");
        string project = Path.Combine(RepoLayout.Root, "src", "tests", name, name + ".csproj");

        if (UsePrebuiltTargets && !File.Exists(dll))
        {
            throw new FileNotFoundException($"Pre-built subprocess '{name}' was not found at '{dll}'.", dll);
        }

        if (!File.Exists(dll))
        {
            // Both helper projects reference SOS.TestHarness and can be initialized concurrently by
            // different test rows. Serialize their fallback builds so their MSBuild nodes do not write
            // the shared harness intermediate assembly at the same time.
            lock (s_subprocessBuildLock)
            {
                if (!File.Exists(dll))
                {
                    RunToCompletion(
                        RepoLayout.DotNetExe,
                        $"build \"{project}\" -c {RepoLayout.ArtifactsConfiguration} -p:TargetArch={RepoLayout.TargetArch}");
                }
            }
        }

        if (!File.Exists(dll))
        {
            throw new InvalidOperationException($"Build of {name} did not produce '{dll}'.");
        }

        return dll;
    }

    private static void RunToCompletion(string fileName, string arguments)
    {
        ProcessStartInfo psi = new()
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = RepoLayout.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using Process p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{fileName} {arguments}' failed ({p.ExitCode}):\n{stdout}\n{stderr}");
        }
    }
}
