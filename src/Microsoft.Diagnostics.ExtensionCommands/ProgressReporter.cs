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
        private readonly int _intervalMs;
        private readonly Stopwatch _stopwatch;
        private long _scannedBytes;
        private long _lastReportMs;

        /// <summary>
        /// Creates a new ProgressReporter.
        /// </summary>
        /// <param name="callback">Invoked periodically with (bytesScanned, totalBytes).</param>
        /// <param name="totalBytes">Total expected bytes to scan.</param>
        /// <param name="intervalMs">Minimum interval in milliseconds between reports.</param>
        public ProgressReporter(Action<long, long> callback, long totalBytes, int intervalMs)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _totalBytes = totalBytes;
            _intervalMs = intervalMs;
            _stopwatch = Stopwatch.StartNew();
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

            long elapsedMs = _stopwatch.ElapsedMilliseconds;
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
            return $"Scanning heap: {scannedBytes / (1024 * 1024):n0} MB / {totalBytes / (1024 * 1024):n0} MB ({pct:f0}%)...";
        }
    }
}
