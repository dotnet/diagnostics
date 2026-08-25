// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// One debugger host that SOS can run inside. A test author programs against this
/// surface without depending on whether the backend is dbgeng, LLDB, or dotnet-dump.
///
/// A host owns exactly one loaded target and is single-threaded with respect to
/// command execution — mirroring the real-world constraint that a dbgeng instance
/// is single-threaded and holds one dump at a time. Parallelism therefore comes
/// from running <em>different</em> hosts/targets concurrently, never from issuing
/// concurrent commands to one host.
/// </summary>
public interface IDebuggerHost : IDisposable
{
    /// <summary>Short host name, e.g. "cdb" or "dotnet-dump". Drives host-conditional assertions.</summary>
    string Name { get; }

    /// <summary>Load the SOS extension into the host. Idempotent.</summary>
    void LoadSos();

    /// <summary>
    /// Run a raw debugger command exactly as typed (no SOS prefixing). Use for engine
    /// commands like <c>.load</c>, <c>~*k</c>, etc.
    /// </summary>
    SosOutput Execute(string command);

    /// <summary>
    /// Run a SOS command. The host applies whatever prefixing it needs (dbgeng wants a
    /// leading <c>!</c>; dotnet-dump takes the bare command), so the test author writes
    /// <c>Sos("clrstack")</c> once and it works everywhere.
    /// </summary>
    SosOutput Sos(string command);
}
