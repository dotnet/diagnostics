// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// The runtime flavor a target is built and dumped as — the third test axis (after host and
/// stop point). Mirrors what ClrMD's test suite covers.
/// </summary>
[Flags]
public enum Flavor
{
    /// <summary>Framework-dependent .NET (net10.0). Self-snapshots via dotnet-dump collect.</summary>
    Core = 1,

    /// <summary>Self-contained single-file publish. Self-snapshots via dotnet-dump collect (no bundled createdump).</summary>
    SingleFile = 2,

    /// <summary>Desktop .NET Framework (net48, Windows-only). No diagnostics IPC — dumps captured externally via DbgEng.</summary>
    Framework = 4,

    /// <summary>
    /// All flavors valid for the platform.
    /// </summary>
    AllValid = Core | SingleFile | Framework,
}
