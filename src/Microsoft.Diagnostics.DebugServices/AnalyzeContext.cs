// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Common context for commands
    /// </summary>
    public class AnalyzeContext
    {
        public AnalyzeContext()
        {
        }

        /// <summary>
        /// Current OS thread Id
        /// </summary>
        public int CurrentThreadId { get; set; }

        /// <summary>
        /// Cancellation token for current command
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Directory of the runtime module (coreclr.dll, libcoreclr.so, etc.)
        /// </summary>
        public string RuntimeModuleDirectory { get; set; }
    }
}