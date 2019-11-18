using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class WriteDumpTests
    {
        private string GetTraceePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "../../../Tracee/Debug/netcoreapp3.0/Tracee.exe";
            }
            return @"../../../Tracee/Debug/netcoreapp3.0/Tracee";
        }

        /// <summary>
        /// A simple test that writes a single dump file
        /// </summary>
        [Fact]
        public void BasicWriteDumpTest()
        {
            var dumpPath = "./myDump.dmp";
            TestRunner runner = new TestRunner(GetTraceePath());
            runner.Start(3000);
            DiagnosticsClient client = new DiagnosticsClient(runner.Pid);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Throws<PlatformNotSupportedException>(() => client.WriteDump(DumpType.Normal, dumpPath));
            }
            else
            {
                client.WriteDump(DumpType.Normal, dumpPath);
                Assert.True(File.Exists(dumpPath));
                File.Delete(dumpPath);
            }
            runner.Stop();
        }

        /// <summary>
        /// A test that writes all the different types of dump file
        /// </summary>
        [Fact]
        public void WriteAllDumpTypesTest()
        {
            var normalDumpPath = "./myDump-normal.dmp";
            var heapDumpPath = "./myDump-heap.dmp";
            var triageDumpPath = "./myDump-triage.dmp";
            var fullDumpPath = "./myDump-full.dmp";
            TestRunner runner = new TestRunner(GetTraceePath());
            runner.Start(3000);
            DiagnosticsClient client = new DiagnosticsClient(runner.Pid);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Throws<PlatformNotSupportedException>(() => client.WriteDump(DumpType.Normal, normalDumpPath));
                Assert.Throws<PlatformNotSupportedException>(() => client.WriteDump(DumpType.WithHeap, heapDumpPath));
                Assert.Throws<PlatformNotSupportedException>(() => client.WriteDump(DumpType.Triage, triageDumpPath));
                Assert.Throws<PlatformNotSupportedException>(() => client.WriteDump(DumpType.Full, fullDumpPath));
            }
            else
            {
                // Write each type of dump
                client.WriteDump(DumpType.Normal, normalDumpPath);
                client.WriteDump(DumpType.WithHeap, heapDumpPath);
                client.WriteDump(DumpType.Triage, triageDumpPath);
                client.WriteDump(DumpType.Full, fullDumpPath);

                // Check they were all created
                Assert.True(File.Exists(normalDumpPath));
                Assert.True(File.Exists(heapDumpPath));
                Assert.True(File.Exists(triageDumpPath));
                Assert.True(File.Exists(fullDumpPath));

                // Remove them
                File.Delete(normalDumpPath);
                File.Delete(heapDumpPath);
                File.Delete(triageDumpPath);
                File.Delete(fullDumpPath);
            }
            runner.Stop();
        }

        /// <summary>
        /// A test that tries to write a dump of a non-existent process
        /// </summary>
        [Fact]
        public void WriteDumpFailTest()
        {
            List<int> pids = new List<int>(DiagnosticsClient.GetPublishedProcesses());
            int arbitraryPid = 1;
            string dumpPath = "./myDump.dmp";
            while (pids.Contains(arbitraryPid))
            {
                arbitraryPid += 1;
            }

            var client = new DiagnosticsClient(arbitraryPid);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Throws<PlatformNotSupportedException>(() => client.WriteDump(DumpType.Normal, dumpPath));
            }
            else
            {
                Assert.Throws<ServerNotAvailableException>(() => client.WriteDump(DumpType.Normal, "./myDump.dmp"));
            }
        }
    }
}
