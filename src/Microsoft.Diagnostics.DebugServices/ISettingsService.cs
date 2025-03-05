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
    }
}
