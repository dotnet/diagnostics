// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Provides the .NET runtime information
    /// </summary>
    public interface IRuntimeService
    {
        /// <summary>
        /// Returns the list of runtimes in the target
        /// </summary>
        IEnumerable<IRuntime> EnumerateRuntimes();
    }
}
