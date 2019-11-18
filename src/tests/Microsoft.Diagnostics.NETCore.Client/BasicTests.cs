using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using Xunit;

using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.NETCore.Client
{
    
    /// <summary>
    /// Suite of tests that test top-level commands
    /// </summary>
    public class BasicTests
    {
        private string GetTraceePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "../../../Tracee/Debug/netcoreapp3.0/Tracee.exe";
            }
            return @"../../../Tracee/Debug/netcoreapp3.0/Tracee";
        }

        [Fact]
        public void PublishedProcessTest1()
        {
            TestRunner runner = new TestRunner(GetTraceePath());
            runner.Start();

            // Sleeping some arbitrary time to let the web app launch and set up the diagnostics server.
            Thread.Sleep(3000);

            List<int> publishedProcesses = new List<int>(DiagnosticsClient.GetPublishedProcesses());
            Assert.Contains(publishedProcesses, p => p == runner.Pid);
            runner.Stop();
        }

        [Fact]
        public void MultiplePublishedProcessTest()
        {
            TestRunner[] runner = new TestRunner[3];
            int[] pids = new int[3];

            for (var i = 0; i < 3; i++)
            {
                runner[i] = new TestRunner(GetTraceePath());
                runner[i].Start();
                pids[i] = runner[i].Pid;
            }

            Thread.Sleep(3000);

            List<int> publishedProcesses = new List<int>(DiagnosticsClient.GetPublishedProcesses());

            for (var i = 0; i < 3; i++)
            {
                Assert.Contains(publishedProcesses, p => p == pids[i]);
            }

            for (var i = 0 ; i < 3; i++)
            {
                runner[i].Stop();
            }
        }
    }
}
