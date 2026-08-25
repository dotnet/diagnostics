// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// The GC flavor the debuggee runs under when captured/launched — a matrix axis (flags, like
/// <see cref="Flavor"/>). Server GC produces a multi-heap GC (a fixed heap count with DATAS off, so it
/// can't collapse back to a single heap), which the eeheap parser and the generation/region tests
/// exercise; Workstation is the single-heap default.
/// </summary>
[Flags]
public enum GcType
{
    /// <summary>Single-heap workstation GC (the runtime default).</summary>
    Workstation = 1,

    /// <summary>Multi-heap server GC (forced to a deterministic heap count via env vars at capture/launch).</summary>
    Server = 2,

    /// <summary>Both GC modes.</summary>
    AllValid = Workstation | Server,
}
