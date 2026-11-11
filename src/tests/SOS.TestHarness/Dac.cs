// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Which DAC (the data-access component SOS reads runtime state through) to debug a target with — a
/// test-matrix axis that applies <b>only to .NET 11+</b>. The <em>same dump</em> is reused across both
/// values; only the DAC SOS loads at debug time differs (driven by <c>runtimes --usecdac</c>), so this
/// axis never multiplies dump capture.
///
/// <para>Default is <see cref="All"/>. <see cref="TestConfig.IsValid"/> prunes <see cref="CDac"/> for any
/// version earlier than .NET 11 (the cDAC doesn't exist there), so a default config collapses to
/// <see cref="Legacy"/>-only on ≤net10 and runs <em>both</em> on net11+. Mask a value off (e.g.
/// <c>Dac.All &amp; ~Dac.CDac</c>) to disable a DAC that hits an unfixable bug for a given test.</para>
/// </summary>
[Flags]
public enum Dac
{
    /// <summary>The classic native DAC (<c>libmscordaccore</c>), selected by the dump's coreclr build.</summary>
    Legacy = 1,

    /// <summary>The cDAC (managed contract reader). Only valid on .NET 11+.</summary>
    CDac = 2,

    /// <summary>Both DAC kinds (pruned to what's valid for the version by <see cref="TestConfig.IsValid"/>).</summary>
    All = Legacy | CDac,
}
