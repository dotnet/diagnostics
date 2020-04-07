// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client
{

    /// <summary>
    /// Suite of tests that test top-level commands
    /// </summary>
    public class GetPublishedProcessesTest
    {
        private readonly ITestOutputHelper output;

        public GetPublishedProcessesTest(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public void PublishedProcessTest1()
        {
            TestRunner runner = new TestRunner(CommonHelper.GetTraceePath(), output);
            runner.Start(3000);
            // On Windows, runner.Start will not wait for named pipe creation since for other tests, NamedPipeClientStream will
            // just wait until the named pipe is created.
            // For these tests, we need to sleep an arbitrary time before pipe is created.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Thread.Sleep(5000);
            }
            List<int> publishedProcesses = new List<int>(DiagnosticsClient.GetPublishedProcesses());
            foreach (int p in publishedProcesses)
            {
                output.WriteLine($"[{DateTime.Now.ToString()}] Saw published process {p}");
            }
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
                runner[i] = new TestRunner(CommonHelper.GetTraceePath(), output);
                runner[i].Start();
                pids[i] = runner[i].Pid;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Thread.Sleep(5000);
            }
            List<int> publishedProcesses = new List<int>(DiagnosticsClient.GetPublishedProcesses());
            foreach (int p in publishedProcesses)
            {
                output.WriteLine($"[{DateTime.Now.ToString()}] Saw published process {p}");
            }

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
