// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Enumeration control flags
    /// </summary>
    [Flags]
    public enum RuntimeEnumerationFlags
    {
        /// <summary>
        /// Providers can return only the runtimes they feel are important like ones involved in a crash.
        /// </summary>
        Default = 0x00,

        /// <summary>
        /// Force enumeration of all runtimes.  If set, all the possible runtimes in the target process are enumerated.
        /// </summary>
        All = 0x01,
    }
}
