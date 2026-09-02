// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// The live "lldb" host (Linux/macOS): launches the debuggee under <c>lldb</c>, parks it at the program
/// entry (before CoreCLR is up) with SOS loaded, and advances it on demand. It is the lldb analogue of
/// <see cref="DbgEngLiveHost"/>: a stateful, advancing target owned exclusively by one test.
///
/// Unlike the Windows engine — which runs in-process and is therefore driven out-of-process through a
/// child <see cref="ChildEngineClient"/> — lldb is already its own process, so this host drives it
/// directly through <see cref="LldbHostBase"/> (the shared spawn/<c>runcommand</c>/sentinel machinery).
///
/// Stop detection is by text: <c>process continue</c> runs synchronously and its output reports either a
/// stop ("<c>Process N stopped</c>") or an exit ("<c>Process N exited</c>"); the precise managed location
/// is confirmed with <c>clrstack</c>, exactly as the dbgeng host confirms with its own clrstack.
/// </summary>
public sealed class LldbLiveHost : LldbHostBase, ILiveDebuggerHost
{
    // A navigation resumes the debuggee through however many internal SOS notification breakpoints (JIT /
    // prestub) it takes to reach the requested managed method. In the healthy case those auto-continue so
    // the method is reached on the first resume; under heavy CPU contention that auto-continue can degrade
    // into many separate stops. We therefore bound the walk by a wall clock (see CommandTimeout) rather
    // than a small fixed resume count, and keep this only as a safety net against a pathological
    // instantly-returning continue so the loop can never spin forever.
    private const int MaxResumes = 10000;

    private readonly Flavor _flavor;
    private readonly Dac _dac;
    private readonly CoreVersion _coreVersion;

    public override string Name => "lldb-live";

    // Live navigation resumes the debuggee and waits for it to reach a managed stop point; under a
    // saturated full-matrix run that can be briefly CPU-starved and take ~2 min, so give it more headroom
    // than the (uniformly fast) dump hosts to avoid flaking on contention. Override with SOSHARNESS_LIVE_TIMEOUT
    // (seconds).
    protected override TimeSpan CommandTimeout { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("SOSHARNESS_LIVE_TIMEOUT"), out int s) && s > 0
            ? TimeSpan.FromSeconds(s)
            : TimeSpan.FromSeconds(300);

    public LldbLiveHost(string exePath, Flavor flavor, CoreVersion coreVersion = CoreVersion.Net10, Dac dac = Dac.Legacy)
    {
        _flavor = flavor;
        _dac = dac;
        _coreVersion = coreVersion;

        // The debuggee inherits the lldb process environment. Disable W^E so SOS's bpmd can patch JIT-ed
        // code (see dotnet/diagnostics#3126), matching what the legacy live lldb harness set. For a
        // framework-dependent (Core) debuggee, point its apphost at the multi-version test runtime install
        // so it binds the runtime matching its target framework (net8 -> 8.0.x, net11 -> the preview).
        //
        // Capture the host's stdout/stderr for diagnosability, but do NOT enable crash-dump collection
        // here: the env would be inherited by the launched debuggee and dump on every intentional crash a
        // "run to crash" test triggers. Only the post-mortem lldb host (no debuggee) opts into dumps.
        StartLldb(psi =>
        {
            psi.Environment["DOTNET_EnableWriteXorExecute"] = "0";
            if (_flavor == Flavor.Core)
            {
                psi.Environment["DOTNET_ROOT"] = RepoLayout.DotnetTestRoot;
                psi.Environment["DOTNET_ROOT(x86)"] = RepoLayout.DotnetTestRoot;
                psi.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            }
        }, diagnostics: new HostDiagnostics(Name));

        Run($"target create \"{exePath}\"");

        if (OperatingSystem.IsMacOS())
        {
            // Keep the debuggee at CoreCLR's normal native-debugger mode. Mode 7 is only for the separate
            // runtime hosted inside Apple LLDB and must not change the target's managed exception behavior.
            Run("settings set target.env-vars PAL_MachExceptionMode=2");
        }

        // Stop at the program entry so we can load SOS and arm bpmd before the app runs.
        Run("process launch -s");

        // A managed fault (divide-by-zero -> SIGFPE, null-deref -> SIGSEGV, etc.) is first delivered to the
        // runtime's signal handler, which turns it into a managed exception. We must therefore pass those
        // signals through to the debuggee without stopping; an *unhandled* managed exception then tears the
        // process down via abort() (SIGABRT), which is the point a live "run to crash" should stop at.
        Run("process handle -s false -n false -p true SIGFPE");
        Run("process handle -s false -n false -p true SIGSEGV");
        Run("process handle -s true -n true -p true SIGABRT");

        LoadSos();
    }

    public override void LoadSos()
    {
        if (_dac == Dac.CDac)
        {
            ToolPaths.EnsureLldbPluginCDacOverride();
        }

        Run($"plugin load \"{ToolPaths.LldbPluginPath}\"");
        Run($"sethostruntime \"{ToolPaths.HostRuntimeDirectory}\"");

        string? dacDir = _dac == Dac.CDac ? ToolPaths.CDacOverrideDirectory : null;
        dacDir ??= _flavor == Flavor.SingleFile ? ToolPaths.SingleFileDacDirectory(_coreVersion) : null;
        if (dacDir is { Length: > 0 })
        {
            Run($"setsymbolserver -directory \"{dacDir}\"");
        }

        // Select the DAC for this config's Dac axis (Legacy => false, CDac on .NET 11+ => true). The
        // SOSHARNESS_USECDAC clamp (off by default; never in CI) overrides it on a skewed dev box.
        Run($"runtimes --usecdac {DacPolicy.UseCDac(_dac)}");
    }

    /// <summary>
    /// Set a managed breakpoint on <paramref name="module"/>!<paramref name="method"/> and run until it is
    /// hit. Throws if the process exits first or the breakpoint is never reached.
    /// </summary>
    public SosOutput RunToBpmd(string module, string method)
    {
        ClearBreakpoints();
        string bpmdOutput = Sos($"bpmd {module} {method}").Text;

        // bpmd reaches the method in stages (a JIT/prestub notification, then the entry), so resume until
        // clrstack confirms we are actually stopped at the requested method. Normally SOS's notification
        // breakpoints auto-continue and the method is reached on the very first resume; under a saturated
        // full-matrix run that auto-continue can degrade into many individual stops, so bound the walk by
        // a wall clock (CommandTimeout) instead of a small fixed count — a slow-but-progressing navigation
        // must still complete, not fail at an arbitrary Nth resume.
        DateTime deadline = DateTime.UtcNow + CommandTimeout;
        int resumes = 0;
        while (DateTime.UtcNow < deadline && resumes < MaxResumes)
        {
            string cont = Execute("process continue").Text;
            resumes++;
            if (StoppedAtMethod(method))
            {
                return new SosOutput(Name, $"bpmd {module} {method}", bpmdOutput);
            }

            if (HasExited(cont) || ProcessIsDead())
            {
                throw new InvalidOperationException(
                    $"Debuggee exited before hitting bpmd {module}!{method} (after {resumes} resume(s)).");
            }
        }

        throw new InvalidOperationException(
            $"Did not reach bpmd {module}!{method} within {CommandTimeout} ({resumes} resume(s)).");
    }

    /// <summary>
    /// Run the process until it crashes (the runtime aborts on an unhandled managed exception, i.e.
    /// SIGABRT). Throws if it exits cleanly without crashing.
    /// </summary>
    public SosOutput RunToCrash()
    {
        ClearBreakpoints();

        DateTime deadline = DateTime.UtcNow + CommandTimeout;
        int resumes = 0;
        while (DateTime.UtcNow < deadline && resumes < MaxResumes)
        {
            string cont = Execute("process continue").Text;
            resumes++;
            if (StoppedOnSignal(cont))
            {
                return new SosOutput(Name, "run-to-crash", cont);
            }

            if (HasExited(cont) || ProcessIsDead())
            {
                throw new InvalidOperationException("Process exited without crashing.");
            }
        }

        throw new InvalidOperationException($"Process did not crash within {CommandTimeout} ({resumes} resume(s)).");
    }

    /// <summary>
    /// Resume to the next breakpoint the caller has already armed (e.g. via <c>Sos("bpmd …")</c>). Sets and
    /// clears nothing itself. Throws if the process exits without hitting one.
    /// </summary>
    public SosOutput RunToBreakpoint()
    {
        string cont = Execute("process continue").Text;
        if (HasExited(cont))
        {
            throw new InvalidOperationException("Process exited without hitting a breakpoint.");
        }

        if (StoppedOnSignal(cont))
        {
            throw new InvalidOperationException($"Hit a fatal signal, not a breakpoint:\n{cont}");
        }

        return new SosOutput(Name, "run-to-breakpoint", cont);
    }

    /// <summary>Drop any breakpoints left from a previous stop point so they don't re-trigger on resume.</summary>
    private void ClearBreakpoints()
    {
        Sos("bpmd -clearall");
        Execute("breakpoint delete --force");
    }

    /// <summary>Is the managed call stack currently topped by <paramref name="method"/>?</summary>
    private bool StoppedAtMethod(string method)
    {
        string stack = Sos("clrstack").Text;
        return stack.Contains(method, StringComparison.Ordinal);
    }

    /// <summary>
    /// True if a <c>process continue</c> reported the debuggee exiting. Covers the normal exit line plus the
    /// symptoms lldb prints when the inferior died out from under a command under contention (a lost gdb-
    /// remote connection, or a subsequent command complaining the process is gone) — any of which means the
    /// debuggee is no longer resumable and we must stop, not keep resuming.
    /// </summary>
    private static bool HasExited(string continueOutput) =>
        continueOutput.Contains("exited with status", StringComparison.OrdinalIgnoreCase) ||
        continueOutput.Contains(" exited ", StringComparison.OrdinalIgnoreCase) ||
        continueOutput.Contains("lost connection", StringComparison.OrdinalIgnoreCase) ||
        continueOutput.Contains("process must be launched", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Authoritatively ask lldb whether the debuggee is gone, instead of only scraping the (human-readable,
    /// and under contention occasionally mis-framed) <c>process continue</c> text. Queries the live
    /// <c>SBProcess</c> state via the script interpreter and treats the terminal states — invalid,
    /// unloaded, detached, exited — as dead. Defensive: if the state can't be read/parsed it returns false
    /// and callers fall back to <see cref="HasExited"/>.
    /// </summary>
    private bool ProcessIsDead()
    {
        // lldb.eStateType: 0 invalid, 1 unloaded, 2 connected, 3 attaching, 4 launching, 5 stopped,
        // 6 running, 7 stepping, 8 crashed, 9 detached, 10 exited, 11 suspended.
        const string Tag = "SOSHARNESS_STATE=";
        string outp = Execute(
            $"script print('{Tag}%d' % lldb.debugger.GetSelectedTarget().GetProcess().GetState())").Text;

        int idx = outp.LastIndexOf(Tag, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false; // couldn't determine — let text-based HasExited decide
        }

        int start = idx + Tag.Length;
        int end = start;
        while (end < outp.Length && char.IsDigit(outp[end]))
        {
            end++;
        }

        if (end == start || !int.TryParse(outp.AsSpan(start, end - start), out int state))
        {
            return false;
        }

        return state is 0 or 1 or 9 or 10; // invalid, unloaded, detached, exited
    }

    /// <summary>True if a <c>process continue</c> stopped on a signal (the runtime's abort = a crash).</summary>
    private static bool StoppedOnSignal(string continueOutput) =>
        continueOutput.Contains("stop reason = signal", StringComparison.OrdinalIgnoreCase);
}
