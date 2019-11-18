using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;

using Microsoft.Diagnostics.TestHelpers;

using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.NETCore.Client
{

    public class TestRunner
    {
        private Process testProcess;
        private ProcessStartInfo startInfo;

        public TestRunner(string testExePath)
        {
            startInfo = new ProcessStartInfo(testExePath);
        }

        public void AddEnvVar(string key, string value)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        public void Start()
        {
            testProcess = Process.Start(startInfo);
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
