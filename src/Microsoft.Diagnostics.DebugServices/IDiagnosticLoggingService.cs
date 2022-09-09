// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Service to control the internal diagnostic (Trace) logging.
    /// </summary>
    public interface IDiagnosticLoggingService
    {
        /// <summary>
        /// Returns true if logging to console or file
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// The file path if logging to file.
        /// </summary>
        string FilePath { get; }

        /// <summary>
        /// Enable diagnostics logging.
        /// </summary>
        /// <param name="filePath">log file path or null if log to console</param>
        /// <remarks>see File.Open for possible exceptions thrown</remarks>
        void Enable(string filePath);

        /// <summary>
        /// Disable diagnostics logging (close if logging to file).
        /// </summary>
        void Disable();
    }
}
