// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>
    /// Receives byte-progress callbacks from heap enumeration and writes a formatted
    /// progress message to the console whenever enough time has elapsed.
    /// </summary>
    internal sealed class ProgressReporter
    {
        /// <summary>
        /// Default minimum interval in milliseconds between progress writes.
        /// </summary>
        public const long DefaultReportingInterval = 5000;

        private readonly Action<string> _writeMessage;
        private readonly long _intervalMs;
        private readonly Func<long> _getElapsedMs;
        private long _lastReportMs;

        /// <summary>
        /// Creates a new ProgressReporter using the system clock.
        /// </summary>
        /// <param name="writeMessage">Called with the formatted progress string to display.</param>
        /// <param name="intervalMs">Minimum interval in milliseconds between writes.</param>
        public ProgressReporter(Action<string> writeMessage, long intervalMs = DefaultReportingInterval)
            : this(writeMessage, intervalMs, getElapsedMs: null)
        {
        }

        /// <summary>
        /// Creates a new ProgressReporter with an injectable time source for testing.
        /// </summary>
        /// <param name="writeMessage">Called with the formatted progress string to display.</param>
        /// <param name="intervalMs">Minimum interval in milliseconds between writes.</param>
        /// <param name="getElapsedMs">
        /// Returns the current elapsed time in milliseconds. When <see langword="null"/>,
        /// a real <see cref="Stopwatch"/> is used.
        /// </param>
        internal ProgressReporter(Action<string> writeMessage, long intervalMs, Func<long> getElapsedMs)
        {
            _writeMessage = writeMessage ?? throw new ArgumentNullException(nameof(writeMessage));
            _intervalMs = intervalMs;
            if (getElapsedMs is not null)
            {
                _getElapsedMs = getElapsedMs;
            }
            else
            {
                Stopwatch sw = Stopwatch.StartNew();
                _getElapsedMs = () => sw.ElapsedMilliseconds;
            }
        }

        /// <summary>
        /// Called by the heap enumerator with the current scan position.
        /// Writes a progress message if enough time has elapsed since the last write.
        /// </summary>
        public void Report(long scannedBytes, long totalBytes)
        {
            long elapsedMs = _getElapsedMs();
            if (elapsedMs - _lastReportMs >= _intervalMs)
            {
                _lastReportMs = elapsedMs;
                _writeMessage(FormatProgressMessage(scannedBytes, totalBytes));
            }
        }

        /// <summary>
        /// Formats a progress message suitable for display during heap scanning.
        /// </summary>
        public static string FormatProgressMessage(long scannedBytes, long totalBytes)
        {
            double pct = totalBytes > 0 ? 100.0 * scannedBytes / totalBytes : 0;
            return FormattableString.Invariant(
                $"Scanning heap: {scannedBytes / (1024 * 1024):n0} MB / {totalBytes / (1024 * 1024):n0} MB ({pct:f0}%)...");
        }
    }
}
