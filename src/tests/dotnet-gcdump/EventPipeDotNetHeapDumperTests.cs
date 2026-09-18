// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Graphs;
using Microsoft.Diagnostics.Tools.GCDump;
using Xunit;

namespace DotnetGCDump.UnitTests
{
    public class EventPipeDotNetHeapDumperTests
    {
        [Fact]
        public void DumpFromEventPipeSerializesLogWrites()
        {
            ConcurrentWriteDetector writer = new();
            DotNetHeapInfo heapInfo = new();
            MemoryGraph memoryGraph = new(50_000);

            bool success = EventPipeDotNetHeapDumper.DumpFromEventPipe(
                CancellationToken.None,
                Environment.ProcessId,
                diagnosticPort: null,
                memoryGraph,
                writer,
                timeout: 30,
                heapInfo);

            Assert.True(success, writer.Output);
            Assert.True(
                writer.ConcurrentWriteCount == 0,
                $"Detected {writer.ConcurrentWriteCount} concurrent writes to the supplied TextWriter.{Environment.NewLine}{writer.Output}");
        }

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

        private sealed class ConcurrentWriteDetector : TextWriter
        {
            private readonly StringBuilder _output = new();
            private int _activeWriter;
            private int _concurrentWriteCount;

            public override Encoding Encoding => Encoding.UTF8;

            public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;

            public int ConcurrentWriteCount => Volatile.Read(ref _concurrentWriteCount);

            public string Output => _output.ToString();

            public override void WriteLine(string value)
            {
                WriteLineCore(value);
            }

            public override void WriteLine(string format, object arg0)
            {
                WriteLineCore(string.Format(FormatProvider, format, arg0));
            }

            public override void WriteLine(string format, object arg0, object arg1)
            {
                WriteLineCore(string.Format(FormatProvider, format, arg0, arg1));
            }

            public override void WriteLine(string format, object arg0, object arg1, object arg2)
            {
                WriteLineCore(string.Format(FormatProvider, format, arg0, arg1, arg2));
            }

            public override void WriteLine(string format, params object[] arg)
            {
                WriteLineCore(string.Format(FormatProvider, format, arg));
            }

            private void WriteLineCore(string value)
            {
                if (Interlocked.CompareExchange(ref _activeWriter, 1, 0) != 0)
                {
                    Interlocked.Increment(ref _concurrentWriteCount);
                    return;
                }

                try
                {
                    _output.AppendLine(value);

                    if (value.StartsWith("Found Module ", StringComparison.Ordinal) ||
                        value.Contains("gcdump EventPipe session shut down", StringComparison.Ordinal))
                    {
                        Thread.Sleep(25);
                    }
                }
                finally
                {
                    Volatile.Write(ref _activeWriter, 0);
                }
            }
        }

        private sealed class ThrowingTextWriter : StringWriter
        {
            private int _throwOnShutdown = 1;

            public ThrowingTextWriter()
                : base(CultureInfo.InvariantCulture)
            {
            }

            public override void WriteLine(string format, object arg0)
            {
                if (format.Contains("gcdump EventPipe session shut down", StringComparison.Ordinal) &&
                    Interlocked.Exchange(ref _throwOnShutdown, 0) != 0)
                {
                    throw new IOException("Simulated shutdown logging failure.");
                }

                base.WriteLine(format, arg0);
            }
        }
    }
}
