// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
        /// Directory of the runtime module (coreclr.dll, libcoreclr.so, etc.)
        /// </summary>
        string RuntimeModuleDirectory { get; set; }

        /// <summary>
        /// Returns the list of runtimes in the target
        /// </summary>
        IEnumerable<IRuntime> EnumerateRuntimes();

        /// <summary>
        /// Returns the current runtime or null if no runtime was found
        /// </summary>
        IRuntime CurrentRuntime { get; }

        /// <summary>
        /// Set the current runtime 
        /// </summary>
        /// <param name="runtimeId">runtime id</param>
        void SetCurrentRuntime(int runtimeId);
    }
}
