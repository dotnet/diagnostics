// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

using Microsoft.Diagnostics.TestHelpers;

using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class TestRunner
    {
        private Process testProcess;
        private ProcessStartInfo startInfo;
        private ITestOutputHelper outputHelper;

        public TestRunner(string testExePath, ITestOutputHelper _outputHelper=null)
        {
            startInfo = new ProcessStartInfo(CommonHelper.HostExe, testExePath);
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            outputHelper = _outputHelper;
        }

        public void AddEnvVar(string key, string value)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        public void Start(int timeoutInMS=0)
        {
            if (outputHelper != null)
                outputHelper.WriteLine("$[{DateTime.Now.ToString()}] Launching test: " + startInfo.FileName);

            testProcess = Process.Start(startInfo);

            if (testProcess == null)
            {
                outputHelper.WriteLine($"Could not start process: " + startInfo.FileName);
            }

            if (testProcess.HasExited)
            {
                outputHelper.WriteLine($"Process " + startInfo.FileName + " came back as exited");
            }

            if (outputHelper != null)
            {
                outputHelper.WriteLine($"[{DateTime.Now.ToString()}] Successfuly started process {testProcess.Id}");
                outputHelper.WriteLine($"Have total {testProcess.Modules.Count} modules loaded");
            }

            outputHelper.WriteLine($"[{DateTime.Now.ToString()}] Sleeping for {timeoutInMS} ms.");
            Thread.Sleep(timeoutInMS);
            outputHelper.WriteLine($"[{DateTime.Now.ToString()}] Done sleeping. Ready to test.");
        }

        public void Stop()
        {
            testProcess.Kill();
        }

        public int Pid {
            get { return testProcess.Id; }
        }

        public void PrintStatus()
        {
            if (testProcess.HasExited)
            {
                outputHelper.WriteLine($"Process {testProcess.Id} status: Exited");
            }
            else
            {
                outputHelper.WriteLine($"Process {testProcess.Id} status: Running");
            }
        }
    }
}
