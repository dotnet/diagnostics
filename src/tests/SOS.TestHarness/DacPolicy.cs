// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Resolves the <c>runtimes --usecdac</c> argument a host issues for a config's <see cref="Dac"/> axis:
/// <see cref="Dac.CDac"/> ⇒ <c>true</c>, <see cref="Dac.Legacy"/> ⇒ <c>false</c>.
///
/// <para><c>SOSHARNESS_USECDAC</c> is a global clamp (off by default; never set in CI). When set it
/// overrides the per-config axis so the whole run forces one DAC — the escape hatch for a dev box whose
/// installed runtimes are skewed such that the cDAC can't load. Setting it collapses both DAC matrix rows
/// onto the same DAC, so the cDAC axis is only meaningful with it unset.</para>
/// </summary>
internal static class DacPolicy
{
    public static string UseCDac(Dac dac)
    {
        string? clamp = Environment.GetEnvironmentVariable("SOSHARNESS_USECDAC");
        if (!string.IsNullOrEmpty(clamp))
        {
            return clamp;
        }

        return dac == Dac.CDac ? "true" : "false";
    }
}
