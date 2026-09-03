// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>Creates the concrete host for a given host kind.</summary>
internal static class HostFactory
{
    /// <summary>Create a dump-backed host.</summary>
    public static IDebuggerHost CreateDumpHost(Host host, Flavor flavor, string dumpPath, Dac dac = Dac.Legacy, CoreVersion coreVersion = CoreVersion.Net10, string? targetExe = null, HostDiagnostics? diagnostics = null) => host switch
    {
        // cdb runs dbgeng in a CHILD process (EngineHost), so the test host never loads dbgeng.
        Host.Cdb => ChildEngineClient.ForDump(host.ToString().ToLowerInvariant(), dumpPath, DacDirFor(flavor, coreVersion), dac),
        Host.DotnetDump => new DotNetDumpHost(dumpPath, flavor, dac, coreVersion, diagnostics),
        Host.Lldb => new LldbCliHost(dumpPath, flavor, dac, coreVersion, targetExe, diagnostics),
        _ => throw new ArgumentException($"Unknown host '{host}'."),
    };

    /// <summary>A live host (exclusive, advancing). On Windows this is the in-process dbgeng engine driven
    /// through a child EngineHost process; on Linux/macOS it drives the lldb CLI directly.</summary>
    public static ILiveDebuggerHost CreateLiveHost(Host host, Flavor flavor, string exePath, CoreVersion coreVersion = CoreVersion.Net10, Dac dac = Dac.Legacy) => host switch
    {
        Host.Cdb => ChildEngineClient.ForLive(host.ToString().ToLowerInvariant(), exePath, DacDirFor(flavor, coreVersion), dac, flavor),
        Host.Lldb => new LldbLiveHost(exePath, flavor, coreVersion, dac),
        Host.DotnetDump => throw new ArgumentException("dotnet-dump is post-mortem only; it has no live host."),
        _ => throw new ArgumentException($"Unknown live host '{host}'."),
    };

    /// <summary>
    /// The DAC directory to make dbgeng load explicitly for a flavor. Self-contained single-file bundles
    /// the runtime, so cdb can't find <c>mscordaccore.dll</c> on disk — point it at the runtime pack's
    /// DAC for the published version. Other flavors find their DAC next to the runtime, so they need no
    /// override.
    /// </summary>
    private static string? DacDirFor(Flavor flavor, CoreVersion coreVersion) =>
        flavor == Flavor.SingleFile ? ToolPaths.SingleFileDacDirectory(coreVersion) : null;
}
