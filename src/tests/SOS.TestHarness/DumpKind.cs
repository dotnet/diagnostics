// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// The kind of dump captured for a target — a matrix axis (flags, like <see cref="Flavor"/>). Selects how
/// much of the process the dump contains, which lets tests validate SOS against reduced dumps.
/// <list type="bullet">
///   <item><see cref="Full"/> — a full dump (createdump type 4 / <c>--type Full</c>) used by tests that need
///   data omitted from reduced dumps.</item>
///   <item><see cref="Heap"/> — the default dump type (createdump type 2 / <c>--type Heap</c>).</item>
///   <item><see cref="Mini"/> — a heap-less minidump (createdump type 1 / <c>--type Mini</c>) used only by
///   commands that can validate meaningful data from the reduced dump.</item>
/// </list>
/// </summary>
[Flags]
public enum DumpKind
{
    /// <summary>Heap dump (createdump type 2 / <c>--type Heap</c>) — the default capture type.</summary>
    Heap = 1,

    /// <summary>Minidump (createdump type 1 / <c>--type Mini</c>) without managed heap memory.</summary>
    Mini = 2,

    /// <summary>Full dump (createdump type 4 / <c>--type Full</c>) with all memory.</summary>
    Full = 4,

    /// <summary>The reduced dump kinds used by default expanded dump-kind coverage.</summary>
    All = Heap | Mini,
}
