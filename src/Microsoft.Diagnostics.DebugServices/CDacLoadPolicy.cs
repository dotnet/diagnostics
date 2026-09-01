// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices;

/// <summary>
/// Controls cDAC activation and DAC fallback policies.
/// </summary>
public enum CDacLoadPolicy
{
    /// <summary>
    /// Attempt cDAC activation and allow DAC fallback.
    /// </summary>
    PreferCDac = 0,

    /// <summary>
    /// Require cDAC activation without DAC fallback.
    /// </summary>
    OnlyUseCDac = 1,

    /// <summary>
    /// Use the DAC without attempting cDAC activation.
    /// </summary>
    UseLegacyDac = 2,

}

/// <summary>
/// Evaluates cDAC activation policy.
/// </summary>
public static class CDacPolicy
{
    /// <summary>
    /// Returns the effective cDAC activation policy.
    /// </summary>
    /// <param name="policy">The configured activation policy.</param>
    /// <returns>The policy to use for activation.</returns>
    public static CDacLoadPolicy GetEffectiveLoadPolicy(CDacLoadPolicy policy)
    {
        if (policy == CDacLoadPolicy.PreferCDac &&
            (Environment.GetEnvironmentVariable("DOTNET_ENABLE_CDAC") == "1" ||
             Environment.GetEnvironmentVariable("COMPlus_ENABLE_CDAC") == "1"))
        {
            // Let the legacy DAC host cDAC instead of loading the standalone cDAC.
            return CDacLoadPolicy.UseLegacyDac;
        }
        return policy;
    }
}
