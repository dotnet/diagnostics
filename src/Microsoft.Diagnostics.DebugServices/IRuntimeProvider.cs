// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Provides the runtime information to the runtime service
    /// </summary>
    public interface IRuntimeProvider
    {
        /// <summary>
        /// Returns the list of runtimes in the target
        /// </summary>
        /// <param name="startingRuntimeId">The starting runtime id for this provider</param>
        IEnumerable<IRuntime> EnumerateRuntimes(int startingRuntimeId);
    }
}
