// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace SOS.TestHarness;

/// <summary>
/// Shared machinery for the lldb-CLI hosts (dump and live). Spawns the <c>lldb</c> binary as a child
/// process, imports <c>lldbhelper.py</c> (which adds a <c>runcommand</c> command), and frames every
/// command as <c>runcommand &lt;cmd&gt;</c> so each one is delimited by a sentinel that also carries a real
/// success bit (<c>&lt;END_COMMAND_OUTPUT&gt;</c> / <c>&lt;END_COMMAND_ERROR&gt;</c> from
/// <c>SBCommandReturnObject.Succeeded()</c>). SOS itself is the native lldb plugin
/// (<c>libsosplugin.so</c>/<c>.dylib</c>); its managed extension runs on the runtime named by
/// <c>sethostruntime</c>. Derived hosts differ only in how they create the target (a core file vs. a
/// launched process) and how they advance it.
/// </summary>
public abstract class LldbHostBase : IDebuggerHost, IDiagnosticHost
{
    private const string EndMarker = "<END_COMMAND_OUTPUT>";
    private const string ErrorMarker = "<END_COMMAND_ERROR>";

    private static readonly string? s_trace = Environment.GetEnvironmentVariable("SOSHARNESS_LLDB_TRACE");
    private static readonly object s_traceLock = new();

    private Process _process = null!;
    private StreamWriter _stdin = null!;
    private readonly BlockingCollection<string> _lines = new();
    private Thread _reader = null!;
    private Thread? _stderrReader;
    private HostDiagnostics? _diagnostics;
    private string? _commandInFlight;

    public abstract string Name { get; }

    /// <summary>Captured stdout/stderr and crash dumps for this host (see <see cref="IDiagnosticHost"/>).</summary>
    public HostDiagnostics? Diagnostics => _diagnostics;

    /// <summary>
    /// How long to wait for a single command's output before declaring lldb wedged. Dump hosts answer from
    /// a static core and are uniformly fast, so the default is tight. The live host overrides this higher:
    /// its <c>process continue</c> must let the debuggee actually run to a managed stop point, which under a
    /// saturated full-matrix run can be briefly CPU-starved and legitimately slow (observed ~2 min), so a
    /// tight timeout would flake on contention rather than catch a real wedge.
    /// </summary>
    protected virtual TimeSpan CommandTimeout => TimeSpan.FromSeconds(120);

    /// <summary>
    /// How long to wait for the heavy, contention-sensitive startup steps — spawning lldb and draining its
    /// banner, and (for the dump host) <c>target create --core</c>. Loading a multi-hundred-MB core under a
    /// saturated full-matrix run can take noticeably longer than a single command (observed ~93s isolated,
    /// &gt;120s under contention), and it shares none of the wedge risk of an interactive command, so it gets
    /// its own, looser budget. Per-command execution keeps the tighter <see cref="CommandTimeout"/> so a
    /// genuinely wedged command still surfaces promptly. Override with <c>SOSHARNESS_LLDB_LOAD_TIMEOUT</c>
    /// (seconds).
    /// </summary>
    protected virtual TimeSpan LoadTimeout { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("SOSHARNESS_LLDB_LOAD_TIMEOUT"), out int s) && s > 0
            ? TimeSpan.FromSeconds(s)
            : TimeSpan.FromSeconds(300);

    /// <summary>
    /// Spawn lldb, import the command helper, and drain the startup banner so the host is ready for
    /// commands. Derived constructors call this first, then create/advance their target.
    /// <paramref name="configure"/> runs against the <see cref="ProcessStartInfo"/> before launch (e.g. to
    /// set debuggee environment variables a live host needs inherited). <paramref name="diagnostics"/>, if
    /// supplied, captures the process's stdout/stderr; when <paramref name="captureCrashDumps"/> is also
    /// set the process runs with the .NET crash-dump environment so a fatal fault in the hosted SOS runtime
    /// writes a dump (only the dump host opts into this — a live host would otherwise also dump its
    /// debuggee's intentional crashes).
    /// </summary>
    protected void StartLldb(Action<ProcessStartInfo>? configure = null, HostDiagnostics? diagnostics = null, bool captureCrashDumps = false)
    {
        _diagnostics = diagnostics;

        string helper = Path.Combine(AppContext.BaseDirectory, "lldbhelper.py");
        if (!File.Exists(helper))
        {
            throw new FileNotFoundException($"lldb command helper not found at '{helper}'.", helper);
        }

        ProcessStartInfo psi = new()
        {
            FileName = ToolPaths.LldbExe,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // --no-lldbinit: ignore the dev's ~/.lldbinit so the session is hermetic.
        // disable-aslr false: toggling ASLR needs ptrace perms we may not have; keep it off so target
        //   creation/launch never fails on that.
        // prompt-on-quit false: never block waiting for a y/n on shutdown.
        psi.ArgumentList.Add("--no-lldbinit");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("settings set target.disable-aslr false");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("settings set interpreter.prompt-on-quit false");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add($"command script import {helper}");

        // Hermetic symbols: scrub any inherited _NT_SYMBOL_PATH (a dev's may point at the Azure-authed
        // symweb). We *remove* it rather than point it at a local cache: the native lldb SOS plugin treats
        // a set _NT_SYMBOL_PATH as the only search root and stops falling back to the on-disk runtime
        // modules, which is how SOS locates the DAC for a locally captured target. Leaving it unset keeps
        // that on-disk resolution working.
        psi.Environment.Remove("_NT_SYMBOL_PATH");

        if (OperatingSystem.IsMacOS())
        {
            // Apple LLDB guards its Mach exception ports. The SOS hosting runtime must not replace them
            // or macOS terminates LLDB with EXC_GUARD (dotnet/diagnostics#4551).
            psi.Environment["PAL_MachExceptionMode"] = "7";

            // sos-lldb links LLDB.framework through @rpath. Resolve it from the selected Xcode at launch
            // rather than embedding the build machine's /Applications/Xcode*.app path in the driver.
            string? sharedFrameworks = ToolPaths.ResolveXcodeSharedFrameworksDirectory();
            if (sharedFrameworks is not null)
            {
                string? inherited = Environment.GetEnvironmentVariable("DYLD_FRAMEWORK_PATH");
                psi.Environment["DYLD_FRAMEWORK_PATH"] = string.IsNullOrEmpty(inherited)
                    ? sharedFrameworks
                    : sharedFrameworks + Path.PathSeparator + inherited;
            }
        }

        // Run the host with the .NET crash-dump environment so a fatal fault in the SOS managed runtime
        // hosted inside lldb writes a full dump we can surface as an artifact. Do this before configure so
        // a derived host could still override it if needed.
        if (captureCrashDumps)
        {
            diagnostics?.ConfigureCrashDumps(psi);
        }

        configure?.Invoke(psi);

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start lldb");
        _stdin = _process.StandardInput;
        _diagnostics?.RecordProcess(_process);

        StreamReader stdout = _process.StandardOutput;
        _reader = new Thread(() => ReadLoop(stdout)) { IsBackground = true, Name = "lldb-reader" };
        _reader.Start();

        // Drain stderr on its own thread: lldb prints crash diagnostics, python errors, and unhandled
        // managed-exception traces there. It was previously redirected but never read, so a full stderr
        // pipe could even block the host — and, more importantly, the evidence for a crash was discarded.
        if (_diagnostics is not null)
        {
            StreamReader stderr = _process.StandardError;
            _stderrReader = new Thread(() => StderrLoop(stderr)) { IsBackground = true, Name = "lldb-stderr" };
            _stderrReader.Start();
        }

        // Drain the startup banner up to the marker the helper prints from __lldb_init_module.
        DrainToMarker(LoadTimeout);
    }

    public abstract void LoadSos();

    /// <summary>Run a raw lldb command verbatim (no SOS dispatch).</summary>
    public SosOutput Execute(string command) => new(Name, command, Run(command));

    /// <summary>Run a SOS command via the plugin's universal <c>sos &lt;command&gt;</c> dispatcher.</summary>
    public SosOutput Sos(string command) => new(Name, command, Run("sos " + command));

    /// <summary>Send a command through the <c>runcommand</c> helper and return its output up to the sentinel.</summary>
    protected string Run(string command) => Run(command, CommandTimeout);

    /// <summary>
    /// As <see cref="Run(string)"/>, but with an explicit timeout — used for the slow, contention-sensitive
    /// load steps (e.g. <c>target create --core</c>) that warrant the looser <see cref="LoadTimeout"/>.
    /// </summary>
    protected string Run(string command, TimeSpan timeout)
    {
        _commandInFlight = command;
        try
        {
            _stdin.WriteLine("runcommand " + command);
            _stdin.Flush();
            string outp = DrainToMarker(timeout, command);
            AppendTrace($"\n>>> lldb={ToolPaths.LldbExe}\n>>> plugin={ToolPaths.LldbPluginPath}\n>>> rt={ToolPaths.HostRuntimeDirectory}\n(lldb) runcommand {command}\n{outp}\n");
            return outp;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            AppendTrace($"--- lldb command failed ---{Environment.NewLine}command={command}{Environment.NewLine}{LldbProcessState()}{Environment.NewLine}");
            throw CreateLldbFailure("lldb command failed", command, ex);
        }
        finally
        {
            _commandInFlight = null;
        }
    }

    /// <summary>
    /// Collect output lines until the sentinel. Strips lldb's prompt-echo lines (<c>(lldb) ...</c>), which
    /// lldb writes for every command when stdin is redirected; SOS output never begins with that prefix, so
    /// this is safe.
    /// </summary>
    private string DrainToMarker(TimeSpan timeout, string? command = null)
    {
        StringBuilder sb = new();
        while (true)
        {
            if (!_lines.TryTake(out string? line, (int)timeout.TotalMilliseconds, HarnessCancellation.Token))
            {
                if (_lines.IsCompleted || HasExited())
                {
                    AppendTrace($"--- lldb stdout closed ---{Environment.NewLine}command={command ?? "<startup>"}{Environment.NewLine}{LldbProcessState()}{Environment.NewLine}");
                    throw CreateLldbFailure("lldb stdout closed", command, null);
                }

                AppendTrace($"--- lldb command timed out ---{Environment.NewLine}command={command ?? "<startup>"}{Environment.NewLine}timeout={timeout}{Environment.NewLine}{LldbProcessState()}{Environment.NewLine}");
                throw CreateLldbFailure("lldb command timed out", command, null);
            }

            string trimmed = line.TrimEnd();
            if (trimmed.EndsWith(EndMarker, StringComparison.Ordinal) || trimmed.EndsWith(ErrorMarker, StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("(lldb) ", StringComparison.Ordinal))
            {
                continue;
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private void ReadLoop(StreamReader stdout)
    {
        try
        {
            string? line;
            while ((line = stdout.ReadLine()) is not null)
            {
                _diagnostics?.AppendStdout(line);
                _lines.Add(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            AppendTrace($"--- lldb stdout read failed ---{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        finally
        {
            _lines.CompleteAdding();
            AppendTrace($"--- lldb stdout eof ---{Environment.NewLine}{LldbProcessState()}{Environment.NewLine}");
        }
    }

    private void StderrLoop(StreamReader stderr)
    {
        try
        {
            string? line;
            while ((line = stderr.ReadLine()) is not null)
            {
                _diagnostics?.AppendStderr(line);
            }
        }
        catch
        {
            // Best effort: the process may die mid-read. Whatever we captured is still available.
        }
    }

    private InvalidOperationException CreateLldbFailure(string phase, string? command, Exception? inner)
    {
        StringBuilder sb = new();
        sb.AppendLine($"phase={phase}");
        sb.AppendLine(LldbProcessState());
        sb.AppendLine($"command={command ?? "<startup>"}");
        sb.AppendLine($"commandInFlight={_commandInFlight ?? "<none>"}");
        sb.AppendLine($"ToolPaths.LldbExe={ToolPaths.LldbExe}");
        sb.AppendLine($"ToolPaths.LldbPluginPath={ToolPaths.LldbPluginPath}");
        sb.AppendLine($"ToolPaths.HostRuntimeDirectory={ToolPaths.HostRuntimeDirectory}");
        sb.AppendLine($"crashDumpDirectory={HostDiagnostics.CrashDumpDirectory}");
        sb.AppendLine($"SOSHARNESS_LLDB_TRACE={s_trace ?? "<unset>"}");

        if (_diagnostics is not null)
        {
            sb.AppendLine($"commandLine={_diagnostics.CommandLine}");
            sb.AppendLine("--- lldb stderr tail ---");
            AppendOrEmpty(sb, _diagnostics.StderrTail());
            sb.AppendLine("--- lldb stdout tail ---");
            AppendOrEmpty(sb, _diagnostics.StdoutTail());
        }

        return new InvalidOperationException(sb.ToString(), inner);
    }

    private static void AppendOrEmpty(StringBuilder sb, string text)
    {
        if (text.Length == 0)
        {
            sb.AppendLine("(empty)");
            return;
        }

        sb.Append(text);
        if (!text.EndsWith('\n'))
        {
            sb.AppendLine();
        }
    }

    private bool HasExited()
    {
        try
        {
            return _process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private string LldbProcessState()
    {
        StringBuilder sb = new();
        try
        {
            sb.AppendLine($"pid={_process.Id}");
            bool hasExited = _process.HasExited;
            sb.AppendLine($"hasExited={hasExited}");
            if (hasExited)
            {
                sb.AppendLine($"exitCode={_process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"processStateError={ex.GetType().Name}: {ex.Message}");
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendTrace(string text)
    {
        if (s_trace is not { Length: > 0 })
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(s_trace);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (s_traceLock)
            {
                File.AppendAllText(s_trace, text);
            }
        }
        catch
        {
            // Trace output is diagnostic only.
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                // Ask lldb to quit. A *wedged* lldb (busy-spinning on its inferior, not reading stdin)
                // never sees this, so don't wait long before escalating to a hard kill.
                try
                {
                    _stdin.WriteLine("quit");
                    _stdin.Flush();
                }
                catch
                {
                    // stdin may already be closed; fall through to the kill path.
                }

                if (!_process.WaitForExit(3000))
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
            // Reap the child (and its debuggee, killed via the process tree above). Without this the
            // killed lldb/debuggee linger as unreaped zombies; across a long multi-version run they
            // accumulate, saturate the box, and wedge later live sessions. A bounded wait keeps teardown
            // from blocking if the kill is still propagating.
            try
            {
                _process.WaitForExit(10000);
            }
            catch
            {
                // best effort
            }

            // The readers can still be inside StreamReader after the process exits. Join them before
            // disposing Process so teardown cannot invalidate StandardOutput/StandardError mid-read.
            _reader.Join(10000);
            _stderrReader?.Join(10000);
            _process.Dispose();
        }
    }
}
