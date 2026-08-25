// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities.DbgEng;

namespace SOS.TestHarness;

/// <summary>
/// Shared machinery for the dbgeng-backed hosts (dump and live). Hosts dbgeng in-process,
/// captures command output via <see cref="DbgEngOutputHolder"/> (no stdout sentinel scraping),
/// and pins every engine call to one dedicated worker thread — honoring dbgeng's
/// single-threaded, one-target-at-a-time nature. Derived classes only implement how the
/// target is opened (open a dump vs. launch-and-run-to-a-breakpoint).
///
/// This type does NOT enforce the "one in-process dbgeng per process" limit itself; that is
/// the job of <see cref="DbgEngArbiter"/>, which decides when hosts may exist.
/// </summary>
public abstract class DbgEngHostBase : IDebuggerHost
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _worker;
    private readonly StringBuilder _buffer = new();

    private IDisposable _clientDisposable = null!;
    private DbgEngOutputHolder _output = null!;
    private bool _sosLoaded;

    /// <summary>The dbgeng client. Only touch from the worker thread.</summary>
    protected IDebugClient Client { get; private set; } = null!;

    /// <summary>The dbgeng control. Only touch from the worker thread.</summary>
    protected IDebugControl Control { get; private set; } = null!;

    public abstract string Name { get; }

    protected DbgEngHostBase()
    {
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "dbgeng-host" };
        _worker.Start();
    }

    /// <summary>Derived ctors call this after their fields are set; runs <see cref="OnOpen"/> on the worker.</summary>
    protected void Initialize()
    {
        try
        {
            Invoke(() => {
                _clientDisposable = IDebugClient.Create(ToolPaths.DbgEngDirectory);
                Client = (IDebugClient)_clientDisposable;
                Control = (IDebugControl)_clientDisposable;

                _output = new DbgEngOutputHolder(Client);
                _output.OutputReceived += (text, _) => _buffer.Append(text);

                // Raw dbgeng leaves SYMOPT_LOAD_LINES off by default, so SOS suppresses the
                // [file @ line] annotation on managed frames (clrstack, !pe, etc). cdb-based hosts
                // (and the legacy SOSRunner) turn line loading on; do the same so source/line
                // resolution matches the dotnet-dump host. Without this, !clrstack never shows
                // source lines under cdb even when the PDB is found.
                RunCore(".lines -e");

                OnOpen();
            });
        }
        catch
        {
            // Release the dbgeng gate and tear down so a failed open doesn't wedge the
            // other dbgeng-backed collection forever.
            Dispose();
            throw;
        }
    }

    /// <summary>Open the target (dump or live). Runs on the worker thread; engine is ready.</summary>
    protected abstract void OnOpen();

    public void LoadSos() => Invoke(LoadSosCore);

    /// <summary>Worker-thread SOS load. Safe to call from <see cref="OnOpen"/>.</summary>
    protected void LoadSosCore()
    {
        if (_sosLoaded)
        {
            return;
        }

        // cdb auto-loads an SOS when it opens a managed dump - notably the classic desktop
        // .NET Framework SOS from C:\Windows\Microsoft.NET\... for desktop dumps, which only
        // exposes the old command names (e.g. `threads`, not `clrthreads`). Unload any such
        // pre-loaded SOS first so the SOS under test is unambiguously OUR build, then load ours
        // and verify via .chain that ours is the one in the chain.
        EnsureNoSosLoaded();

        // For self-contained single-file dumps the runtime (and DAC) is bundled in the exe, so dbgeng
        // can't find mscordaccore.dll on disk. When the harness tells us where the matching DAC lives,
        // set its load path before loading SOS so the DAC is available when SOS initializes. (Mirrors
        // `.cordll -ve -u -lp <dir>` from the use-local-sos workflow.)
        string? dacDir = Environment.GetEnvironmentVariable("SOSHARNESS_DAC_DIR");
        if (!string.IsNullOrEmpty(dacDir))
        {
            RunCore($".cordll -ve -u -lp {dacDir}");

            // .cordll can cause dbgeng to auto-load an SOS after the initial cleanup. Re-assert a clean
            // extension chain immediately before loading the build under test.
            EnsureNoSosLoaded();
        }

        RunCore($".load {ToolPaths.SosPath}");
        VerifyOurSosLoaded();

        _sosLoaded = true;
    }

    /// <summary>
    /// Remove any SOS cdb auto-loaded, looping over .chain until none remain. Throws if an SOS is
    /// still present after the unload attempts - we must not .load ours on top of a stale SOS we
    /// can't remove, because commands could route to the wrong SOS.
    /// </summary>
    private void EnsureNoSosLoaded()
    {
        const int MaxUnloads = 5;
        for (int i = 0; i <= MaxUnloads; i++)
        {
            string chain = RunCore(".chain");
            if (!ChainContainsSos(chain))
            {
                return; // chain is clean - safe to load ours
            }

            if (i == MaxUnloads)
            {
                throw new InvalidOperationException(
                    $"Could not unload pre-loaded SOS before loading ours; .chain still shows an SOS after {MaxUnloads} attempts:\n{chain}");
            }

            // .unload sos removes the extension named "sos" (cdb's auto-loaded one). Harmless if
            // none is loaded - cdb just reports it and we ignore that.
            RunCore(".unload sos");
        }
    }

    /// <summary>Confirm our SOS (and only ours) is in the extension chain after loading it.</summary>
    private void VerifyOurSosLoaded()
    {
        string chain = RunCore(".chain");
        if (chain.IndexOf(ToolPaths.SosPath, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException(
                $"Expected our SOS '{ToolPaths.SosPath}' to be loaded, but .chain was:\n{chain}");
        }
    }

    private static bool ChainContainsSos(string chain) =>
        chain.Contains("sos.dll", StringComparison.OrdinalIgnoreCase)
        || chain.Contains("\\sos:", StringComparison.OrdinalIgnoreCase);

    public SosOutput Execute(string command) => new(Name, command, Invoke(() => RunCore(command)));

    public SosOutput Sos(string command) => new(Name, command, Invoke(() => RunCore("!" + command)));

    /// <summary>Worker-thread command execution returning captured output.</summary>
    protected string RunCore(string command)
    {
        _buffer.Clear();
        Control.Execute(DEBUG_OUTCTL.THIS_CLIENT, command, DEBUG_EXECUTE.DEFAULT);
        return _buffer.ToString();
    }

    public void Dispose()
    {
        try
        {
            Invoke(() => {
                _output?.Dispose();
                Client?.EndSession(DEBUG_END.ACTIVE_TERMINATE);
                _clientDisposable?.Dispose();
            });
        }
        catch
        {
            // best effort teardown
        }
        finally
        {
            _work.CompleteAdding();
        }
    }

    // ---- worker marshaling ---------------------------------------------------------------

    private void WorkerLoop()
    {
        foreach (Action action in _work.GetConsumingEnumerable())
        {
            action();
        }
    }

    private protected void Invoke(Action action)
    {
        using ManualResetEventSlim done = new();
        Exception? error = null;
        _work.Add(() => {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        if (error is not null)
        {
            throw new InvalidOperationException($"{Name} host operation failed: {error.Message}", error);
        }
    }

    private protected T Invoke<T>(Func<T> func)
    {
        T result = default!;
        Invoke(() => { result = func(); });
        return result;
    }
}
