// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.Utilities.DbgEng;

namespace SOS.TestHarness;

/// <summary>
/// The live "cdb" host: launches the debuggee under in-process dbgeng and stops at the loader
/// breakpoint with SOS loaded, ready to set managed breakpoints. Unlike the dump hosts this is
/// a <em>stateful, advancing</em> target — it is owned exclusively by one test, never shared.
///
/// <see cref="RunToBpmd"/> sets a managed breakpoint on a method and runs to it, which is how
/// both the stop-point-driven navigation (<see cref="LiveTarget.GoToStopPoint"/>) and raw bpmd
/// tests reach a precise point.
/// </summary>
public sealed class DbgEngLiveHost : DbgEngHostBase
{
    private readonly string _commandLine;
    private readonly string _workingDirectory;

    public override string Name => "cdb-live";

    public DbgEngLiveHost(string exePath)
    {
        _commandLine = $"\"{exePath}\"";
        _workingDirectory = Path.GetDirectoryName(exePath)!;
        Initialize();
    }

    protected override void OnOpen()
    {
        // Stop at the loader breakpoint so we can load SOS and arm bpmd before the app runs.
        Control.AddEngineOptions(DEBUG_ENGOPT.INITIAL_BREAK);

        DEBUG_CREATE_PROCESS_OPTIONS options = new() { CreateFlags = DEBUG_CREATE_PROCESS.DEBUG_ONLY_THIS_PROCESS };
        int hr = Client.CreateProcessAndAttach(_commandLine, _workingDirectory, DEBUG_ATTACH.DEFAULT, in options);
        if (hr < 0)
        {
            throw new InvalidOperationException($"CreateProcessAndAttach('{_commandLine}') failed: 0x{hr:x8}");
        }

        Control.WaitForEvent(TimeSpan.FromSeconds(60));
        RequireStatus(DEBUG_STATUS.BREAK, "initial break");

        LoadSosCore();
    }

    /// <summary>
    /// Set a managed breakpoint on <paramref name="module"/>!<paramref name="method"/> and run
    /// until it is hit. Throws if the process exits first or the breakpoint is never reached.
    /// </summary>
    public SosOutput RunToBpmd(string module, string method)
    {
        string bpmdOutput = string.Empty;

        Invoke(() => {
            // Clear any breakpoints left from a previous stop point. Otherwise a stale bpmd (e.g. the
            // still-pending AtGen0 breakpoint) gets resolved and re-hit the moment we resume toward
            // the next one, stranding us at the old location instead of advancing.
            RunCore("!bpmd -clearall");
            RunCore("bc *");

            bpmdOutput = RunCore($"!bpmd {module} {method}");

            // bpmd reaches the method in two breaks (a JIT/prestub notification, then the entry), so
            // resume until clrstack confirms we are actually stopped at the requested method.
            const int MaxResumes = 50;
            for (int i = 0; i < MaxResumes; i++)
            {
                RunCore("g");
                Control.WaitForEvent(TimeSpan.FromSeconds(60));
                Control.GetExecutionStatus(out DEBUG_STATUS status);

                if (status == DEBUG_STATUS.NO_DEBUGGEE)
                {
                    throw new InvalidOperationException($"Debuggee exited before hitting bpmd {module}!{method}.");
                }

                if (status == DEBUG_STATUS.BREAK && StoppedAtMethod(method))
                {
                    return;
                }
            }

            throw new InvalidOperationException($"Did not reach bpmd {module}!{method} after {MaxResumes} resumes.");
        });

        return new SosOutput(Name, $"bpmd {module} {method}", bpmdOutput);
    }

    /// <summary>Worker-thread check: is the managed call stack currently topped by <paramref name="method"/>?</summary>
    private bool StoppedAtMethod(string method)
    {
        string stack = RunCore("!clrstack");
        return stack.Contains(method, StringComparison.Ordinal);
    }

    /// <summary>
    /// Run the process until it crashes (a second-chance exception break). Throws if it exits
    /// cleanly without crashing. Used by the live <c>GoToCrash</c> path.
    /// </summary>
    public SosOutput RunToCrash()
    {
        Invoke(() => {
            // Drop any bpmd breakpoints from earlier stop points so they don't break before the crash.
            RunCore("!bpmd -clearall");
            RunCore("bc *");

            const int MaxResumes = 25;
            for (int i = 0; i < MaxResumes; i++)
            {
                RunCore("g");
                Control.WaitForEvent(TimeSpan.FromSeconds(60));
                Control.GetExecutionStatus(out DEBUG_STATUS status);

                if (status == DEBUG_STATUS.BREAK)
                {
                    return; // second-chance crash break
                }

                if (status == DEBUG_STATUS.NO_DEBUGGEE)
                {
                    throw new InvalidOperationException("Process exited without crashing.");
                }
            }

            throw new InvalidOperationException($"Process did not crash after {MaxResumes} resumes.");
        });

        return new SosOutput(Name, "run-to-crash", string.Empty);
    }

    /// <summary>
    /// Resume the process to the next breakpoint. Throws if the process exits without hitting one,
    /// or if it stops on a second-chance exception (a crash) rather than a breakpoint. The caller is
    /// responsible for arming the breakpoint (e.g. <c>Sos("bpmd …")</c>); this sets/clears nothing.
    /// </summary>
    public SosOutput RunToBreakpoint()
    {
        const uint StatusBreakpoint = 0x80000003; // int3 — a real breakpoint, not a crash

        Invoke(() => {
            RunCore("g");
            Control.WaitForEvent(TimeSpan.FromSeconds(60));
            Control.GetExecutionStatus(out DEBUG_STATUS status);

            if (status == DEBUG_STATUS.NO_DEBUGGEE)
            {
                throw new InvalidOperationException("Process exited without hitting a breakpoint.");
            }

            // GetLastEvent is true only when the stop was an exception. A second-chance exception
            // whose code isn't a break instruction is a crash, not the breakpoint we ran to.
            if (Control.GetLastEvent(out DEBUG_LAST_EVENT_INFO_EXCEPTION ex, out _, out string? description)
                && ex.FirstChance == 0
                && ex.ExceptionRecord.ExceptionCode != StatusBreakpoint)
            {
                throw new InvalidOperationException(
                    $"Hit a second-chance exception (0x{ex.ExceptionRecord.ExceptionCode:x8}), not a breakpoint: {description}");
            }
        });

        return new SosOutput(Name, "run-to-breakpoint", string.Empty);
    }

    private void RequireStatus(DEBUG_STATUS expected, string phase)
    {
        Control.GetExecutionStatus(out DEBUG_STATUS status);
        if (status != expected)
        {
            throw new InvalidOperationException($"Expected status {expected} at {phase}, but was {status}.");
        }
    }
}
