// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// A live, advancing debugger host: an <see cref="IDebuggerHost"/> that additionally drives a running
/// debuggee forward. Where a dump host opens a frozen target, a live host launches the debuggee, parks it
/// at the loader break with SOS loaded, and then advances it on demand — to a managed method
/// (<see cref="RunToBpmd"/>), to its crash (<see cref="RunToCrash"/>), or to the next armed breakpoint
/// (<see cref="RunToBreakpoint"/>).
///
/// This is the seam that lets <see cref="LiveTarget"/> be backend-agnostic: the Windows backend is the
/// in-process dbgeng engine (driven out-of-process through <see cref="ChildEngineClient"/>), and the
/// Linux/macOS backend drives the <c>lldb</c> CLI. A test author programs against <see cref="Target"/>
/// and never sees which one is underneath.
/// </summary>
public interface ILiveDebuggerHost : IDebuggerHost
{
    /// <summary>Set a managed breakpoint on <paramref name="module"/>!<paramref name="method"/> and run to
    /// it. Throws if the process exits or crashes before reaching the method.</summary>
    SosOutput RunToBpmd(string module, string method);

    /// <summary>Run the process until it crashes (a second-chance/fatal fault). Throws if it exits
    /// cleanly without crashing.</summary>
    SosOutput RunToCrash();

    /// <summary>Resume to the next breakpoint the caller has already armed (e.g. via <c>Sos("bpmd …")</c>).
    /// Sets/clears nothing itself. Throws if the process exits without hitting one.</summary>
    SosOutput RunToBreakpoint();
}
