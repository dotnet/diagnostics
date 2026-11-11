// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
