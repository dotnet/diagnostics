// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>
    /// Reports progress periodically during heap enumeration based on elapsed time.
    /// </summary>
    internal sealed class ProgressReporter
    {
        private readonly Action<long, long> _callback;
        private readonly long _totalBytes;
        private readonly long _intervalMs;
        private readonly Func<long> _getElapsedMs;
        private long _scannedBytes;
        private long _lastReportMs;

        /// <summary>
        /// Creates a new ProgressReporter using the system clock.
        /// </summary>
        /// <param name="callback">Invoked periodically with (bytesScanned, totalBytes).</param>
        /// <param name="totalBytes">Total expected bytes to scan.</param>
        /// <param name="intervalMs">Minimum interval in milliseconds between reports.</param>
        public ProgressReporter(Action<long, long> callback, long totalBytes, long intervalMs)
            : this(callback, totalBytes, intervalMs, getElapsedMs: null)
        {
        }

        /// <summary>
        /// Creates a new ProgressReporter with an injectable time source for testing.
        /// </summary>
        /// <param name="callback">Invoked periodically with (bytesScanned, totalBytes).</param>
        /// <param name="totalBytes">Total expected bytes to scan.</param>
        /// <param name="intervalMs">Minimum interval in milliseconds between reports.</param>
        /// <param name="getElapsedMs">
        /// Returns the current elapsed time in milliseconds. When <see langword="null"/>,
        /// a real <see cref="Stopwatch"/> is used.
        /// </param>
        internal ProgressReporter(Action<long, long> callback, long totalBytes, long intervalMs, Func<long> getElapsedMs)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _totalBytes = totalBytes;
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
        /// Gets the total number of bytes scanned so far.
        /// </summary>
        public long ScannedBytes => _scannedBytes;

        /// <summary>
        /// Reports that an object of the given size has been scanned.
        /// Invokes the callback if enough time has elapsed since the last report.
        /// </summary>
        public void ReportObject(long objectSize)
        {
            _scannedBytes += objectSize;

            long elapsedMs = _getElapsedMs();
            if (elapsedMs - _lastReportMs >= _intervalMs)
            {
                _lastReportMs = elapsedMs;
                _callback(_scannedBytes, _totalBytes);
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
