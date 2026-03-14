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
        /// If true, uses the CDAC contract reader if available.
        /// </summary>
        bool UseContractReader { get; set; }

        /// <summary>
        /// If true, always use the CDAC contract reader even when not requested
        /// </summary>
        bool ForceUseContractReader { get; set; }
    }
}
