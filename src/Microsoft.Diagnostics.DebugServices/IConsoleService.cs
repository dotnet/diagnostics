// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Console output service
    /// </summary>
    public interface IConsoleService
    {
        /// <summary>
        /// Write text to console's standard out
        /// </summary>
        /// <param name="value">text</param>
        void Write(string value);

        /// <summary>
        /// Write warning text to console
        /// </summary>
        /// <param name="value"></param>
        void WriteWarning(string value);

        /// <summary>
        /// Write error text to console
        /// </summary>
        /// <param name="value"></param>
        void WriteError(string value);

        /// <summary>
        /// Cancellation token for current command
        /// </summary>
        CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Screen or window width or 0.
        /// </summary>
        int WindowWidth { get; }
    }
}
