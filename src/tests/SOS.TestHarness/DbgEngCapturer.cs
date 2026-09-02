// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities.DbgEng;

namespace SOS.TestHarness;

/// <summary>
/// Captures dumps with in-process dbgeng for flavors that can't self-collect: desktop
/// <see cref="Flavor.Framework"/> (no diagnostics IPC) and self-contained
/// <see cref="Flavor.SingleFile"/> crash dumps (the single-file bundle doesn't ship/launch
/// createdump). Launches the debuggee under dbgeng once and drives it through every stop point:
/// <see cref="StopKind.Snapshot"/> stops are reached via a managed <c>bpmd</c> breakpoint on the
/// marker method and dumped with dbgeng's <c>.dump</c> command; the <see cref="StopKind.Crash"/> stop is
/// reached by running to the second-chance exception.
///
/// Holds the dbgeng exclusive lease for the duration so it never collides with a shared cdb host.
/// </summary>
public static class DbgEngCapturer
{
    public static void Capture(string exePath, TargetDefinition target, string dumpDir, DumpKind dumpKind)
    {
        using IDisposable lease = HostSlot.DbgEng.AcquireExclusive();

        using IDisposable clientDisposable = IDebugClient.Create(ToolPaths.DbgEngDirectory);
        IDebugClient client = (IDebugClient)clientDisposable;
        IDebugControl control = (IDebugControl)clientDisposable;

        StringBuilder buffer = new();
        using DbgEngOutputHolder output = new(client);
        output.OutputReceived += (text, _) => buffer.Append(text);

        string Run(string command)
        {
            buffer.Clear();
            control.Execute(DEBUG_OUTCTL.THIS_CLIENT, command, DEBUG_EXECUTE.DEFAULT);
            return buffer.ToString();
        }

        try
        {
            control.AddEngineOptions(DEBUG_ENGOPT.INITIAL_BREAK);

            DEBUG_CREATE_PROCESS_OPTIONS options = new() { CreateFlags = DEBUG_CREATE_PROCESS.DEBUG_ONLY_THIS_PROCESS };
            int hr = client.CreateProcessAndAttach($"\"{exePath}\"", Path.GetDirectoryName(exePath), DEBUG_ATTACH.DEFAULT, in options);
            if (hr < 0)
            {
                throw new InvalidOperationException($"CreateProcessAndAttach('{exePath}') failed: 0x{hr:x8}");
            }

            control.WaitForEvent(TimeSpan.FromSeconds(60)); // initial loader break
            Run($".load {ToolPaths.SosPath}");

            // For desktop, the managed module is the EXE itself (e.g. GcPromotion.exe), not the .dll.
            string bpmdModule = Path.GetFileName(exePath);

            foreach (StopPoint stop in target.StopPoints)
            {
                string dumpPath = Path.Combine(dumpDir, stop.Name + ".dmp");

                if (stop.Kind == StopKind.Snapshot)
                {
                    if (stop.Method is null)
                    {
                        throw new InvalidOperationException($"Snapshot stop '{stop.Name}' has no bpmd method.");
                    }

                    Run($"!bpmd {bpmdModule} {stop.Method}");
                    RunToBreak(control, $"bpmd {stop.Method}");
                }
                else // Crash
                {
                    RunToBreak(control, "second-chance crash", requireSecondChanceException: true);
                }

                Run($".dump /o {DbgEngDumpOption(dumpKind)} \"{dumpPath}\"");
                if (!File.Exists(dumpPath))
                {
                    throw new InvalidOperationException($"DbgEng capture failed to write '{dumpPath}'.");
                }
            }
        }
        finally
        {
            try
            {
                client.EndSession(DEBUG_END.ACTIVE_TERMINATE);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static string DbgEngDumpOption(DumpKind dumpKind) => dumpKind switch
    {
        DumpKind.Full => "/ma",
        DumpKind.Heap => "/mw",
        DumpKind.Mini => "/m",
        _ => throw new ArgumentOutOfRangeException(nameof(dumpKind), dumpKind, "Unsupported dump kind"),
    };

    private static void RunToBreak(IDebugControl control, string what, bool requireSecondChanceException = false)
    {
        const int MaxResumes = 40;
        for (int i = 0; i < MaxResumes; i++)
        {
            control.Execute(DEBUG_OUTCTL.THIS_CLIENT, "g", DEBUG_EXECUTE.DEFAULT);
            control.WaitForEvent(TimeSpan.FromSeconds(60));
            control.GetExecutionStatus(out DEBUG_STATUS status);

            if (status == DEBUG_STATUS.BREAK
                && (!requireSecondChanceException
                    || (control.GetLastEvent(out DEBUG_LAST_EVENT_INFO_EXCEPTION exception, out _, out _)
                        && exception.FirstChance == 0)))
            {
                return;
            }

            if (status == DEBUG_STATUS.NO_DEBUGGEE)
            {
                throw new InvalidOperationException($"Debuggee exited before reaching {what}.");
            }
        }

        throw new InvalidOperationException($"Did not reach {what} after {MaxResumes} resumes.");
    }
}
