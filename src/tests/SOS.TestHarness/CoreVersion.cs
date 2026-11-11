// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// The .NET Core runtime version a target is built and dumped against — a test-matrix axis. The bit for a
/// version is its major number (so <see cref="Net8"/> is bit 8, <see cref="Net10"/> bit 10, ...), which
/// keeps the values self-describing and leaves room up to .NET 31.
///
/// <para><b>Semantics: this is a DISABLE mechanism, not a positive declaration of support.</b> A test
/// defaults to <see cref="All"/> (every bit set) and runs against every version the harness actually
/// builds and installs (see <see cref="CoreVersions.Available"/>); bits for versions that aren't built are
/// silently ignored. To stop a test running on a version that hits an unfixable runtime/DAC bug, mask the
/// bit off — e.g. <c>CoreVersion.All &amp; ~CoreVersion.Net8</c> ("everything except .NET 8"). Never use it
/// to positively enumerate "the versions this works on"; the built set is the source of truth for what
/// runs, and the mask only ever removes from it.</para>
/// </summary>
[Flags]
public enum CoreVersion : uint
{
    /// <summary>
    /// No .NET Core version. Used for the desktop <see cref="Flavor.Framework"/> flavor, whose runtime is
    /// desktop .NET Framework (clr.dll), not a .NET Core version — the axis is inert there, so every Framework
    /// config collapses to this single value instead of fanning out one meaningless row per Core version.
    /// </summary>
    None = 0,

    /// <summary>.NET 8.</summary>
    Net8 = 1u << 8,

    /// <summary>.NET 9.</summary>
    Net9 = 1u << 9,

    /// <summary>.NET 10.</summary>
    Net10 = 1u << 10,

    /// <summary>.NET 11.</summary>
    Net11 = 1u << 11,

    /// <summary>.NET 12 (not yet built; reserved so a test can pre-emptively opt in/out).</summary>
    Net12 = 1u << 12,

    /// <summary>
    /// Every version. Intersected with <see cref="CoreVersions.Available"/> at matrix-expansion time, so a
    /// test left at the default runs against exactly the versions the harness builds and installs — minus the
    /// out-of-support set (<see cref="CoreVersions.OutOfSupport"/>), which is excluded from the default
    /// matrix unless opted into via <see cref="CoreVersions.TestOutOfSupportCore"/> or named explicitly in
    /// <c>SOSHARNESS_ONLY_COREVERSIONS</c>.
    /// </summary>
    All = uint.MaxValue,
}
