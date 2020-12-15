// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;


using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Grape
{
    public class TestRunner
    {
        private Process testProcess;
        private ProcessStartInfo startInfo;

        public TestRunner(string testExePath, string argument=null)
        {
            startInfo = new ProcessStartInfo(testExePath, argument);
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
        }

        public void AddEnvVar(string key, string value)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        public void Start(int timeoutInMS=0)
        {
            Debug.WriteLine($"[{DateTime.Now.ToString()}] Launching test: " + startInfo.FileName);
            testProcess = Process.Start(startInfo);

            if (testProcess == null)
            {
                Debug.WriteLine($"Could not start process: " + startInfo.FileName);
            }

            if (testProcess.HasExited)
            {
                Debug.WriteLine($"Process " + startInfo.FileName + " came back as exited");
            }

            Debug.WriteLine($"[{DateTime.Now.ToString()}] Successfuly started process {testProcess.Id}");
            Debug.WriteLine($"Have total {testProcess.Modules.Count} modules loaded");

            Debug.WriteLine($"[{DateTime.Now.ToString()}] Sleeping for {timeoutInMS} ms.");
            Thread.Sleep(timeoutInMS);
            Debug.WriteLine($"[{DateTime.Now.ToString()}] Done sleeping. Ready to test.");
        }

        public void Stop()
        {
            testProcess.Kill();
        }

        public int Pid {
            get { return testProcess.Id; }
        }

    }
}
