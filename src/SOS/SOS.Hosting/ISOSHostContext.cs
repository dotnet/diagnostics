// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;

namespace SOS
{
    /// <summary>
    /// Context/services provided to the SOS host.
    /// </summary>
    public interface ISOSHostContext
    {
        /// <summary>
        /// Display text on the console
        /// </summary>
        /// <param name="text">message</param>
        void Write(string text);

        /// <summary>
        /// Get/set the current native thread id
        /// </summary>
        int CurrentThreadId { get; set; }

        /// <summary>
        /// Cancellation token for current operation
        /// </summary>
        CancellationToken CancellationToken { get; }
    }
}