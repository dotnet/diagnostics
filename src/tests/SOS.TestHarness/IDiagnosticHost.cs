// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Optional capability on a host that captures its underlying process's stdout/stderr and any crash dump
/// (see <see cref="HostDiagnostics"/>). Only the child-process hosts on Linux/macOS (lldb, dotnet-dump)
/// implement it; the in-process dbgeng hosts do not, so callers probe for it with <c>as</c> rather than
/// requiring it on every <see cref="IDebuggerHost"/>.
/// </summary>
public interface IDiagnosticHost
{
    /// <summary>The captured diagnostics for this host, or null if none are being collected.</summary>
    HostDiagnostics? Diagnostics { get; }
}
