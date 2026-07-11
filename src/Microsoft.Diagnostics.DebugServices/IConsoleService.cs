// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Console output service
    /// </summary>
    public interface IConsoleService
    {
        /// <summary>
        /// Gets whether <see cref="OutputType.Dml"/> is supported.
        /// </summary>
        bool SupportsDml { get; }

        /// <summary>
        /// Screen or window width or 0.
        /// </summary>
        int WindowWidth { get; }

        /// <summary>
        /// Cancellation token for current command
        /// </summary>
        CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Writes text to the console
        /// </summary>
        /// <param name="type">type of text to write</param>
        /// <param name="text">text to write</param>
        void WriteString(OutputType type, string text);
    }
}
