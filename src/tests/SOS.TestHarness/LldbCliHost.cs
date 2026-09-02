// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// The dump (post-mortem) "lldb" host (Linux/macOS): opens a core file under <c>lldb</c> and runs SOS
/// against it. The lldb-driving machinery (spawn, <c>runcommand</c> framing, sentinel draining, dispose)
/// lives in <see cref="LldbHostBase"/>; this type only opens the core and points SOS at the right DAC.
///
/// SOS is the native lldb plugin (<c>libsosplugin.so</c>/<c>.dylib</c>), loaded via <c>plugin load</c>;
/// its managed extension is hosted on the runtime named by <c>sethostruntime</c>. SOS commands are
/// dispatched through the plugin's universal <c>sos &lt;command&gt;</c> entry (the lldb analogue of
/// dbgeng's <c>!command</c>), so a test author writes <c>Sos("clrstack")</c> once and it works on every
/// host.
/// </summary>
public sealed class LldbCliHost : LldbHostBase
{
    private readonly Flavor _flavor;
    private readonly Dac _dac;
    private readonly CoreVersion _coreVersion;

    public override string Name => "lldb";

    public LldbCliHost(string dumpPath, Flavor flavor, Dac dac = Dac.Legacy, CoreVersion coreVersion = CoreVersion.Net10, string? targetExe = null, HostDiagnostics? diagnostics = null)
    {
        _flavor = flavor;
        _dac = dac;
        _coreVersion = coreVersion;
        StartLldb(diagnostics: diagnostics, captureCrashDumps: true);

        // Load the core. Pass the target executable as the module so lldb can map the program image —
        // essential for self-contained single-file bundles (coreclr is embedded in the exe) and for
        // createdump-generated crash cores, whose notes alone don't let lldb locate the single-file
        // module (SOS then reports "Failed to find runtime module (libcoreclr.so)"). Mirrors the legacy
        // SOSRunner, which always passed the host exe. SOS is loaded later in LoadSos.
        string create = string.IsNullOrEmpty(targetExe)
            ? $"target create --core \"{dumpPath}\""
            : $"target create --core \"{dumpPath}\" \"{targetExe}\"";
        Run(create, LoadTimeout);
    }

    public override void LoadSos()
    {
        if (_dac == Dac.CDac)
        {
            ToolPaths.EnsureLldbPluginCDacOverride();
        }

        Run($"plugin load \"{ToolPaths.LldbPluginPath}\"");
        Run($"sethostruntime \"{ToolPaths.HostRuntimeDirectory}\"");

        // Self-contained single-file bundles carry coreclr inside the exe, so there is no runtime
        // directory on disk next to which SOS can find the matching DAC. Point SOS's symbol store at the
        // runtime pack's native directory (which ships the DAC the publish resolved against); SOS then
        // resolves the DAC for the dump's coreclr build-id from there. This is a *local directory*
        // (no network), so the session stays hermetic. Other flavors find their DAC next to the on-disk
        // runtime and need no override. (cdb does the equivalent via `.cordll -lp`.)
        string? dacDir = _dac == Dac.CDac ? ToolPaths.CDacOverrideDirectory : null;
        dacDir ??= _flavor == Flavor.SingleFile ? ToolPaths.SingleFileDacDirectory(_coreVersion) : null;
        if (dacDir is { Length: > 0 })
        {
            Run($"setsymbolserver -directory \"{dacDir}\"");
        }

        // Select the DAC for this config's Dac axis: Legacy => `--usecdac false`, CDac (.NET 11+ only) =>
        // `--usecdac true`. The same dump is reused across both, so only this debug-time toggle differs.
        // SOSHARNESS_USECDAC (off by default; never set in CI) is a global clamp that overrides the axis on
        // a dev box whose installed runtimes are skewed such that the cDAC can't load.
        Run($"runtimes --usecdac {DacPolicy.UseCDac(_dac)}");
    }
}
