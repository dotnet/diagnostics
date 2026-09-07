// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
#if !NETFRAMEWORK
using SOS.TestHarness;
#endif

/// <summary>
/// The one piece of shared machinery the marker debuggee uses. A call to <see cref="Stop"/> marks a
/// named point of interest in the running program. The harness realizes that same marker two ways:
/// <list type="bullet">
///   <item><b>Snapshot (capture mode):</b> when <c>SOSHARNESS_CAPTURE_DIR</c> is set, the call
///   snapshots this process (via the repo-built <c>dotnet-dump collect</c> by PID, located through
///   <c>SOSHARNESS_DOTNET</c> + <c>SOSHARNESS_DOTNETDUMP_DLL</c>) into <c>&lt;name&gt;.dmp</c> and lets
///   the process keep running. One run drops every named dump.</item>
///   <item><b>Live (under a debugger):</b> the env var is absent, so the call is a no-op. The harness
///   instead sets a managed <c>bpmd</c> breakpoint on the marker method to pause here.</item>
/// </list>
/// Mark the methods that call <see cref="Stop"/> <c>[MethodImpl(MethodImplOptions.NoInlining)]</c> so
/// they survive as real, breakpointable methods.
/// </summary>
public static class TestHarness
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Stop(string name)
    {
        string? captureDir = Environment.GetEnvironmentVariable("SOSHARNESS_CAPTURE_DIR");
        if (string.IsNullOrEmpty(captureDir))
        {
            // Live mode: nothing to do; the debugger pauses here via bpmd on the caller.
            return;
        }

#if NETFRAMEWORK
        // Desktop .NET Framework has no diagnostics IPC, so it cannot self-snapshot via
        // dotnet-dump collect. Desktop dumps are captured externally by the harness (DbgEng).
        _ = name;
#else
        string dotnet = Environment.GetEnvironmentVariable("SOSHARNESS_DOTNET") ?? "dotnet";
        string? dumpDll = Environment.GetEnvironmentVariable("SOSHARNESS_DOTNETDUMP_DLL");
        if (string.IsNullOrEmpty(dumpDll))
        {
            throw new InvalidOperationException("SOSHARNESS_DOTNETDUMP_DLL is not set; cannot self-snapshot.");
        }

        string outPath = Path.Combine(captureDir, name + ".dmp");

        // Snapshot self by PID and continue. dotnet-dump collect is the supported way to dump a running
        // process without stopping it, and it works for both framework-dependent and self-contained
        // single-file apps.
        ProcessStartInfo psi = new(dotnet)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(dumpDll);
        psi.ArgumentList.Add("collect");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add("--type");
        psi.ArgumentList.Add(Environment.GetEnvironmentVariable("SOSHARNESS_DUMP_TYPE") ?? "Heap");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outPath);

        BoundedProcessResult result = BoundedProcess.Run(
            psi,
            TimeSpan.FromMinutes(2),
            isolateLinuxProcessGroup: true);
        if (result.ExitCode != 0 || !File.Exists(outPath))
        {
            throw new InvalidOperationException(
                $"Snapshot '{name}' failed (exit {result.ExitCode}):\n" +
                $"stdout:\n{result.StandardOutput}\n" +
                $"stderr:\n{result.StandardError}");
        }
#endif
    }
}
