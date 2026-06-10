// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Settings service
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// If true, enforces the proper DAC certificate signing when loaded
        /// </summary>
        bool DacSignatureVerificationEnabled { get; set; }

        /// <summary>
        /// Controls whether the cDAC is used in place of the in-box DAC:
        /// <list type="bullet">
        ///   <item><c>null</c> (default): evaluate policy and fall back. The cDAC is used
        ///     when the target runtime supports it and a matching cDAC is available next
        ///     to the diagnostics tool; otherwise the in-box DAC is used.</item>
        ///   <item><c>true</c>: always use the cDAC. Runtime construction fails if no
        ///     matching cDAC is available.</item>
        ///   <item><c>false</c>: always use the in-box DAC. The cDAC is never loaded.</item>
        /// </list>
        /// </summary>
        bool? UseCDac { get; set; }
    }
}
