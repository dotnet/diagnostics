// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Diagnostics.ExtensionCommands.UnitTests
{
    public class ProgressReporterTests
    {
        // ─── FormatProgressMessage ───────────────────────────────────────────────

        [Fact]
        public void FormatProgressMessage_FormatsCorrectly()
        {
            string msg = ProgressReporter.FormatProgressMessage(
                scannedBytes: 5L * 1024 * 1024 * 1024,  // 5 GB → 5,120 MB
                totalBytes: 16L * 1024 * 1024 * 1024);   // 16 GB → 16,384 MB (31%)

            Assert.Equal("Scanning heap: 5,120 MB / 16,384 MB (31%)...", msg);
        }

        [Fact]
        public void FormatProgressMessage_HandlesZeroTotal()
        {
            string msg = ProgressReporter.FormatProgressMessage(0, 0);
            Assert.Equal("Scanning heap: 0 MB / 0 MB (0%)...", msg);
        }

        [Fact]
        public void FormatProgressMessage_Handles100Percent()
        {
            string msg = ProgressReporter.FormatProgressMessage(1024 * 1024, 1024 * 1024); // 1 MB / 1 MB
            Assert.Equal("Scanning heap: 1 MB / 1 MB (100%)...", msg);
        }

        // ─── ScannedBytes tracking ───────────────────────────────────────────────

        [Fact]
        public void ReportObject_TracksScannedBytes()
        {
            ProgressReporter reporter = new(
                (_, _) => { },
                totalBytes: 1000,
                intervalMs: 60_000, // long interval so callback doesn't fire
                getElapsedMs: () => 0);

            reporter.ReportObject(100);
            Assert.Equal(100, reporter.ScannedBytes);

            reporter.ReportObject(250);
            Assert.Equal(350, reporter.ScannedBytes);

            reporter.ReportObject(50);
            Assert.Equal(400, reporter.ScannedBytes);
        }

        // ─── Timing-based callback tests (deterministic via fake clock) ──────────

        [Fact]
        public void ReportObject_WithZeroInterval_CallsCallbackEveryTime()
        {
            List<(long scanned, long total)> reports = new();
            long fakeMs = 0;

            ProgressReporter reporter = new(
                (scanned, total) => reports.Add((scanned, total)),
                totalBytes: 1000,
                intervalMs: 0,
                getElapsedMs: () => fakeMs);

            reporter.ReportObject(100);
            reporter.ReportObject(200);
            reporter.ReportObject(300);

            Assert.Equal(3, reports.Count);
            Assert.Equal((100, 1000), reports[0]);
            Assert.Equal((300, 1000), reports[1]);
            Assert.Equal((600, 1000), reports[2]);
        }

        [Fact]
        public void ReportObject_DoesNotFireBeforeInterval()
        {
            int callbackCount = 0;
            long fakeMs = 0;

            ProgressReporter reporter = new(
                (_, _) => callbackCount++,
                totalBytes: 1000,
                intervalMs: 10_000,
                getElapsedMs: () => fakeMs);

            // First call at t=0ms: elapsed (0) - lastReport (0) = 0, not >= 10_000, no fire
            reporter.ReportObject(1);
            Assert.Equal(0, callbackCount);

            // Advance to just before the interval boundary; still no fire
            fakeMs = 9_999;
            reporter.ReportObject(1);
            Assert.Equal(0, callbackCount);
        }

        [Fact]
        public void ReportObject_FiresCallbackAfterInterval()
        {
            int callbackCount = 0;
            long fakeMs = 0;

            ProgressReporter reporter = new(
                (_, _) => callbackCount++,
                totalBytes: 1000,
                intervalMs: 10_000,
                getElapsedMs: () => fakeMs);

            // t=0ms: 0 - 0 = 0, not >= 10_000 → no fire
            reporter.ReportObject(1);
            Assert.Equal(0, callbackCount);

            // t=10_000ms: 10_000 - 0 = 10_000, >= 10_000 → fires; lastReport becomes 10_000
            fakeMs = 10_000;
            reporter.ReportObject(1);
            Assert.Equal(1, callbackCount);

            // t=15_000ms: 15_000 - 10_000 = 5_000, not >= 10_000 → no fire
            fakeMs = 15_000;
            reporter.ReportObject(1);
            Assert.Equal(1, callbackCount);

            // t=20_000ms: 20_000 - 10_000 = 10_000, >= 10_000 → fires; lastReport becomes 20_000
            fakeMs = 20_000;
            reporter.ReportObject(1);
            Assert.Equal(2, callbackCount);
        }

        [Fact]
        public void ReportObject_ReportsCorrectBytesInCallback()
        {
            List<(long scanned, long total)> reports = new();
            long fakeMs = 0;

            ProgressReporter reporter = new(
                (scanned, total) => reports.Add((scanned, total)),
                totalBytes: 2000,
                intervalMs: 10_000,
                getElapsedMs: () => fakeMs);

            // t=0ms: no fire yet; bytes accumulate
            reporter.ReportObject(500);
            Assert.Empty(reports);

            // t=10_000ms: fires with accumulated bytes
            fakeMs = 10_000;
            reporter.ReportObject(300);
            Assert.Single(reports);
            Assert.Equal((800L, 2000L), reports[0]);
        }
    }
}
