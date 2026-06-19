// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Controls whether the cDAC is used in place of the in-box DAC.
    /// </summary>
    public enum CDacLoadPolicy
    {
        /// <summary>
        /// Evaluate policy and fall back. The cDAC is used when the target runtime supports it
        /// and a matching cDAC is available next to the diagnostics tool; otherwise the in-box
        /// DAC is used.
        /// </summary>
        Default,

        /// <summary>
        /// Always use the cDAC. Runtime construction fails if no matching cDAC is available.
        /// </summary>
        UseCDac,

        /// <summary>
        /// Always use the in-box DAC. The cDAC is never loaded.
        /// </summary>
        UseLegacyDac,
    }
}
