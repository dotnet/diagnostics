// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Diagnostics.ExtensionCommands.UnitTests
{
    public class ProgressReporterTests
    {
        [Fact]
        public void ReportObject_WithZeroInterval_CallsCallbackEveryTime()
        {
            List<(long scanned, long total)> reports = new();

            ProgressReporter reporter = new(
                (scanned, total) => reports.Add((scanned, total)),
                totalBytes: 1000,
                intervalMs: 0);

            reporter.ReportObject(100);
            reporter.ReportObject(200);
            reporter.ReportObject(300);

            Assert.Equal(3, reports.Count);
            Assert.Equal((100, 1000), reports[0]);
            Assert.Equal((300, 1000), reports[1]);
            Assert.Equal((600, 1000), reports[2]);
        }

        [Fact]
        public void ReportObject_TracksScannedBytes()
        {
            ProgressReporter reporter = new(
                (_, _) => { },
                totalBytes: 1000,
                intervalMs: 60_000); // long interval so callback doesn't fire after first

            reporter.ReportObject(100);
            Assert.Equal(100, reporter.ScannedBytes);

            reporter.ReportObject(250);
            Assert.Equal(350, reporter.ScannedBytes);

            reporter.ReportObject(50);
            Assert.Equal(400, reporter.ScannedBytes);
        }

        [Fact]
        public void ReportObject_WithLongInterval_DoesNotFireDuringInterval()
        {
            int callbackCount = 0;

            ProgressReporter reporter = new(
                (_, _) => callbackCount++,
                totalBytes: 1000,
                intervalMs: 60_000); // 60 seconds - won't fire in this test

            // No calls should fire within the 60s interval
            for (int i = 0; i < 100; i++)
            {
                reporter.ReportObject(1);
            }

            Assert.Equal(0, callbackCount);
            Assert.Equal(100, reporter.ScannedBytes);
        }

        [Fact]
        public void FormatProgressMessage_FormatsCorrectly()
        {
            string msg = ProgressReporter.FormatProgressMessage(
                scannedBytes: 5L * 1024 * 1024 * 1024,  // 5 GB
                totalBytes: 16L * 1024 * 1024 * 1024);   // 16 GB

            Assert.Contains("5", msg);
            Assert.Contains("16", msg);
            Assert.Contains("31%", msg);
            Assert.Contains("Scanning heap:", msg);
        }

        [Fact]
        public void FormatProgressMessage_HandlesZeroTotal()
        {
            string msg = ProgressReporter.FormatProgressMessage(0, 0);
            Assert.Contains("0%", msg);
        }

        [Fact]
        public void FormatProgressMessage_Handles100Percent()
        {
            string msg = ProgressReporter.FormatProgressMessage(1024 * 1024, 1024 * 1024);
            Assert.Contains("100%", msg);
        }
    }
}
