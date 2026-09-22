// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Graphs;
using Microsoft.Diagnostics.Tools.GCDump;
using Xunit;

namespace DotnetGCDump.UnitTests
{
    public class EventPipeDotNetHeapDumperTests
    {
        [Fact]
        public void DumpFromEventPipeReturnsFalseWhenShutdownLoggingFails()
        {
            using ThrowingTextWriter writer = new();
            DotNetHeapInfo heapInfo = new();
            MemoryGraph memoryGraph = new(50_000);

            bool success = EventPipeDotNetHeapDumper.DumpFromEventPipe(
                CancellationToken.None,
                Environment.ProcessId,
                diagnosticPort: null,
                memoryGraph,
                TextWriter.Synchronized(writer),
                timeout: 30,
                heapInfo);

            Assert.False(success);
            Assert.Contains("[Error] Exception during gcdump:", writer.ToString());
        }

        [Fact]
        public void DumpFromEventPipeFileDoesNotReuseLiveCollectionState()
        {
            using StringWriter liveWriter = new(CultureInfo.InvariantCulture);
            DotNetHeapInfo liveHeapInfo = new();
            MemoryGraph liveMemoryGraph = new(50_000);

            bool liveSuccess = EventPipeDotNetHeapDumper.DumpFromEventPipe(
                CancellationToken.None,
                Environment.ProcessId,
                diagnosticPort: null,
                liveMemoryGraph,
                TextWriter.Synchronized(liveWriter),
                timeout: 30,
                liveHeapInfo);

            Assert.True(liveSuccess, liveWriter.ToString());

            string tracePath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tracePath, "not a nettrace file");
                using StringWriter fileWriter = new(CultureInfo.InvariantCulture);
                DotNetHeapInfo fileHeapInfo = new();
                MemoryGraph fileMemoryGraph = new(50_000);

                bool fileSuccess = EventPipeDotNetHeapDumper.DumpFromEventPipeFile(
                    tracePath,
                    fileMemoryGraph,
                    TextWriter.Synchronized(fileWriter),
                    fileHeapInfo);

                Assert.False(fileSuccess);
                Assert.Contains("[Error] Exception processing events:", fileWriter.ToString());
            }
            finally
            {
                File.Delete(tracePath);
            }
        }

        private sealed class ThrowingTextWriter : StringWriter
        {
            private int _throwOnShutdown = 1;

            public ThrowingTextWriter()
                : base(CultureInfo.InvariantCulture)
            {
            }

            public override void WriteLine(string value)
            {
                ThrowIfShutdownMessage(value);
                base.WriteLine(value);
            }

            public override void WriteLine(string format, object arg0)
            {
                ThrowIfShutdownMessage(format);
                base.WriteLine(format, arg0);
            }

            private void ThrowIfShutdownMessage(string value)
            {
                if (value.Contains("gcdump EventPipe session shut down", StringComparison.Ordinal) &&
                    Interlocked.Exchange(ref _throwOnShutdown, 0) != 0)
                {
                    throw new IOException("Simulated shutdown logging failure.");
                }
            }
        }
    }
}
