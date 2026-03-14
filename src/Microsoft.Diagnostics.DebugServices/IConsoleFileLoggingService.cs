// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Console file logging control service
    /// </summary>
    public interface IConsoleFileLoggingService
    {
        /// <summary>
        /// The log file path if enabled, otherwise null.
        /// </summary>
        string FilePath { get; }

        /// <summary>
        /// Enable console file logging.
        /// </summary>
        /// <param name="filePath">log file path</param>
        /// <remarks>see File.Open for possible exceptions thrown</remarks>
        void Enable(string filePath);

        /// <summary>
        /// Disable/close console file logging.
        /// </summary>
        void Disable();

        /// <summary>
        /// Add to the list of file streams to write the console output.
        /// </summary>
        /// <param name="stream">Stream to add. Lifetime managed by caller.</param>
        void AddStream(Stream stream);

        /// <summary>
        /// Remove the specified file stream from the writers.
        /// </summary>
        /// <param name="stream">Stream passed to add. Stream not closed or disposed.</param>
        void RemoveStream(Stream stream);
    }
}
