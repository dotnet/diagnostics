// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

        // ─── Timing-based write tests (deterministic via fake clock) ──────────

        [Fact]
        public void Report_WithZeroInterval_WritesEveryTime()
        {
            int writeCount = 0;
            long fakeMs = 0;

            ProgressReporter reporter = new(
                _ => writeCount++,
                intervalMs: 0,
                getElapsedMs: () => fakeMs);

            reporter.Report(100, 1000);
            reporter.Report(300, 1000);
            reporter.Report(600, 1000);

            Assert.Equal(3, writeCount);
        }

        [Fact]
        public void Report_DoesNotWriteBeforeInterval()
        {
            int writeCount = 0;
            long fakeMs = 0;

            ProgressReporter reporter = new(
                _ => writeCount++,
                intervalMs: 10_000,
                getElapsedMs: () => fakeMs);

            // First call at t=0ms: elapsed (0) - lastReport (0) = 0, not >= 10_000, no write
            reporter.Report(1, 1000);
            Assert.Equal(0, writeCount);

            // Advance to just before the interval boundary; still no write
            fakeMs = 9_999;
            reporter.Report(2, 1000);
            Assert.Equal(0, writeCount);
        }

        [Fact]
        public void Report_WritesAfterInterval()
        {
            int writeCount = 0;
            long fakeMs = 0;

            ProgressReporter reporter = new(
                _ => writeCount++,
                intervalMs: 10_000,
                getElapsedMs: () => fakeMs);

            // t=0ms: 0 - 0 = 0, not >= 10_000 → no write
            reporter.Report(1, 1000);
            Assert.Equal(0, writeCount);

            // t=10_000ms: 10_000 - 0 = 10_000, >= 10_000 → writes; lastReport becomes 10_000
            fakeMs = 10_000;
            reporter.Report(2, 1000);
            Assert.Equal(1, writeCount);

            // t=15_000ms: 15_000 - 10_000 = 5_000, not >= 10_000 → no write
            fakeMs = 15_000;
            reporter.Report(3, 1000);
            Assert.Equal(1, writeCount);

            // t=20_000ms: 20_000 - 10_000 = 10_000, >= 10_000 → writes; lastReport becomes 20_000
            fakeMs = 20_000;
            reporter.Report(4, 1000);
            Assert.Equal(2, writeCount);
        }

        [Fact]
        public void Report_WritesFormattedMessageWithCorrectBytes()
        {
            List<string> messages = new();
            long fakeMs = 0;

            ProgressReporter reporter = new(
                messages.Add,
                intervalMs: 10_000,
                getElapsedMs: () => fakeMs);

            // t=0ms: no write yet
            reporter.Report(500 * 1024 * 1024, 2000 * 1024 * 1024);
            Assert.Empty(messages);

            // t=10_000ms: writes with the current scanned/total values
            fakeMs = 10_000;
            reporter.Report(800 * 1024 * 1024, 2000 * 1024 * 1024);
            Assert.Single(messages);
            Assert.Equal("Scanning heap: 800 MB / 2,000 MB (40%)...", messages[0]);
        }
    }
}
