// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace SOS.TestHarness;

/// <summary>
/// The test-host side of the subprocess dbgeng backend: spawns an <c>EngineHost</c> child that
/// hosts dbgeng in-process and drives it over <see cref="EngineProtocol"/>. From the test host's
/// perspective this is just another child-process REPL (like <see cref="DotNetDumpHost"/>), so the
/// test host never loads dbgeng/SOS/DAC and can't be crashed by them. Because each target is its
/// own child process, many can be alive at once — lifting the single-instance limit that
/// in-process dbgeng imposed.
///
/// The child blocks on stdin between commands (no busy-wait), so idle clients are cheap.
/// </summary>
public sealed class ChildEngineClient : ILiveDebuggerHost
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly BlockingCollection<string> _lines = new();
    private readonly Thread _reader;

    public string Name { get; }

    private ChildEngineClient(string name, string mode, IReadOnlyList<string> modeArgs, string? dacDir, Dac dac, Flavor flavor)
    {
        Name = name;

        ProcessStartInfo psi = new()
        {
            FileName = RepoLayout.DotNetExe,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(SnapshotStore.EngineHostDll);
        psi.ArgumentList.Add(mode);
        foreach (string a in modeArgs)
        {
            psi.ArgumentList.Add(a);
        }

        // _NT_SYMBOL_PATH is ALWAYS fully replaced with a harness-constructed value - never inherited
        // (the dev's ambient _NT_SYMBOL_PATH may point at the Azure-authed symweb, which crashes SOS host
        // init and makes tests network-dependent).
        Directory.CreateDirectory(RepoLayout.SymbolCache);
        string symbolPath = RepoLayout.SymbolCache;

        // For self-contained single-file the runtime (coreclr.dll) and the DAC (mscordaccore.dll) are
        // bundled in the exe, so dbgeng can't find them on disk. Point it at the runtime pack that has
        // both: on the symbol/image path so dbgeng can index coreclr, and via SOSHARNESS_DAC_DIR so the
        // EngineHost runs `.cordll -lp <dir>` (DAC load path) before `.load sos`.
        if (!string.IsNullOrEmpty(dacDir))
        {
            symbolPath += ";" + dacDir;
            psi.Environment["_NT_EXECUTABLE_IMAGE_PATH"] = dacDir;
            psi.Environment["SOSHARNESS_DAC_DIR"] = dacDir;
        }

        psi.Environment["_NT_SYMBOL_PATH"] = symbolPath;

        if (mode == "live")
        {
            // bpmd patches JIT code when binding managed breakpoints. Disable W^X for live
            // debuggees so those writes do not trigger access violations and wedge the target.
            psi.Environment["DOTNET_EnableWriteXorExecute"] = "0";
        }

        // The live debuggee is launched by the EngineHost (via CreateProcessAndAttach) and inherits its
        // environment. For a framework-dependent (Core) live target, point the apphost at the multi-version
        // test runtime install so it binds the runtime matching its target framework (net8 -> 8.0.x,
        // net9 -> 9.0.x, net11 -> the installed preview). Without this the debuggee inherits the ambient
        // DOTNET_ROOT (the product .dotnet, which carries only the repo's own runtime, e.g. net10), so a
        // net9/net11 apphost fails framework resolution ("Framework 'Microsoft.NETCore.App' version 'x' not
        // found") and exits at launch — before bpmd can bind — surfacing as "Process exited without hitting
        // a breakpoint". Dump mode launches nothing, and SingleFile/Framework don't use a shared runtime, so
        // this only applies to a live Core target. Mirrors LldbLiveHost and the dump-capture path
        // (SnapshotStore.ApplyRuntimeRoot). MULTILEVEL_LOOKUP=0 keeps resolution strictly within the test
        // install (no machine-wide fallback), so the debuggee's coreclr is the deterministic on-disk one.
        if (mode == "live" && flavor == Flavor.Core)
        {
            psi.Environment["DOTNET_ROOT"] = RepoLayout.DotnetTestRoot;
            psi.Environment["DOTNET_ROOT(x86)"] = RepoLayout.DotnetTestRoot;
            psi.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        }

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start EngineHost");
        _stdin = _process.StandardInput;

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"enginehost-reader-{name}" };
        _reader.Start();

        WaitForReady(TimeSpan.FromSeconds(120));

        // Select the DAC for this config's Dac axis (Legacy => false, CDac on .NET 11+ => true). SOS is
        // already loaded by the time the child reports ready, so issue it now. The same dump is reused
        // across both DAC values — only this debug-time toggle differs.
        Send("!runtimes --usecdac " + DacPolicy.UseCDac(dac));
    }

    /// <summary>A child engine over a crash/snapshot dump.</summary>
    public static ChildEngineClient ForDump(string hostName, string dumpPath, string? dacDir = null, Dac dac = Dac.Legacy) =>
        new(hostName, "dump", new[] { dumpPath }, dacDir, dac, Flavor.Core);

    /// <summary>A live child engine that launches the target (parked at the loader break, SOS loaded).</summary>
    public static ChildEngineClient ForLive(string hostName, string exePath, string? dacDir = null, Dac dac = Dac.Legacy, Flavor flavor = Flavor.Core) =>
        new(hostName, "live", new[] { exePath }, dacDir, dac, flavor);

    public void LoadSos()
    {
        // The child already loads SOS when it opens the target; nothing to do.
    }

    public SosOutput Execute(string command) => new(Name, command, Send(command));

    public SosOutput Sos(string command) => new(Name, command, Send("!" + command));

    /// <summary>Live only: set a managed breakpoint and run to it (handled inside the child).</summary>
    public SosOutput RunToBpmd(string module, string method) =>
        new(Name, $"bpmd {module} {method}", Send(EngineProtocol.RunToBpmdPrefix + module + " " + method));

    /// <summary>Live only: run the process to its second-chance crash (handled inside the child).</summary>
    public SosOutput RunToCrash() =>
        new(Name, "run-to-crash", Send(EngineProtocol.RunToCrash));

    /// <summary>Live only: resume to the next breakpoint (handled inside the child).</summary>
    public SosOutput RunToBreakpoint() =>
        new(Name, "run-to-breakpoint", Send(EngineProtocol.RunToBreak));

    private string Send(string command)
    {
        _stdin.WriteLine(command);
        _stdin.Flush();
        return DrainToEnd(TimeSpan.FromSeconds(120), command);
    }

    private void WaitForReady(TimeSpan timeout)
    {
        while (true)
        {
            if (!_lines.TryTake(out string? line, (int)timeout.TotalMilliseconds, HarnessCancellation.Token))
            {
                throw new TimeoutException("EngineHost did not become ready in time.");
            }

            if (line == EngineProtocol.Ready)
            {
                return;
            }
        }
    }

    private string DrainToEnd(TimeSpan timeout, string command)
    {
        StringBuilder sb = new();
        while (true)
        {
            if (!_lines.TryTake(out string? line, (int)timeout.TotalMilliseconds, HarnessCancellation.Token))
            {
                throw new TimeoutException($"EngineHost did not return output for '{command}' within {timeout}.");
            }

            if (line == EngineProtocol.End)
            {
                break;
            }

            if (line == EngineProtocol.Error)
            {
                // The child threw while processing this command (e.g. RunToBreakpoint hit a crash or
                // the process exited). Surface it as an exception rather than returning silently with
                // a dead session that later commands would fail against.
                throw new InvalidOperationException(
                    $"EngineHost command '{command}' failed: {sb.ToString().TrimEnd()}");
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private void ReadLoop()
    {
        string? line;
        while ((line = _process.StandardOutput.ReadLine()) is not null)
        {
            _lines.Add(line);
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _stdin.Close(); // EOF -> child's ReadLine returns null -> clean exit
                if (!_process.WaitForExit(5000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // best effort
        }
        finally
        {
            _process.Dispose();
        }
    }
}
