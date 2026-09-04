// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>The known debugger hosts. Tests parameterize over these (xunit serializes enums).</summary>
[Flags]
public enum Host
{
    /// <summary>In-process dbgeng, run in a child EngineHost (Windows).</summary>
    Cdb = 1,

    /// <summary>LLDB debugger host (Linux/macOS).</summary>
    Lldb = 2,

    /// <summary>Managed <c>dotnet-dump analyze</c> host (all OSes).</summary>
    DotnetDump = 4,

    /// <summary>
    /// All hosts that can be used for the given liveness on this platform.
    /// </summary>
    AllValid = Cdb | Lldb | DotnetDump,
}
