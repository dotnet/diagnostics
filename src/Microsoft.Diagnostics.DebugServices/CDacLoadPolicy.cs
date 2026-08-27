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
    /// Returns whether cDAC activation should be attempted.
    /// </summary>
    /// <param name="policy">The requested activation policy.</param>
    /// <returns><see langword="true"/> if cDAC activation should be attempted.</returns>
    public static bool ShouldTryCDac(CDacLoadPolicy policy)
    {
        if (policy == CDacLoadPolicy.OnlyUseCDac)
        {
            return true;
        }
        if (policy == CDacLoadPolicy.UseLegacyDac)
        {
            return false;
        }

        // These variables select the in-box DAC's cDAC integration.
        return Environment.GetEnvironmentVariable("DOTNET_ENABLE_CDAC") != "1"
            && Environment.GetEnvironmentVariable("COMPlus_ENABLE_CDAC") != "1";
    }
}
