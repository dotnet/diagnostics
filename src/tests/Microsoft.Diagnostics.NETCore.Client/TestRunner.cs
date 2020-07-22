// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.TestHelpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class TestRunner
    {
        private Process testProcess;
        private ProcessStartInfo startInfo;
        private ITestOutputHelper outputHelper;

        public TestRunner(string testExePath, ITestOutputHelper _outputHelper = null,
            bool redirectError = false, bool redirectInput = false)
        {
            startInfo = new ProcessStartInfo(CommonHelper.HostExe, testExePath);
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = redirectError;
            startInfo.RedirectStandardInput = redirectInput;
            outputHelper = _outputHelper;
        }

        public void AddEnvVar(string key, string value)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        public StreamWriter StandardInput => testProcess.StandardInput;
        public StreamReader StandardOutput => testProcess.StandardOutput;
        public StreamReader StandardError => testProcess.StandardError;

        public void Start(int timeoutInMS=15000)
        {
            if (outputHelper != null)
                outputHelper.WriteLine($"[{DateTime.Now.ToString()}] Launching test: " + startInfo.FileName);

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
                outputHelper.WriteLine($"[{DateTime.Now.ToString()}] Successfully started process {testProcess.Id}");
                // Retry getting the module count because we can catch the process during startup and it fails temporarily.
                for (int retry = 0; retry < 5; retry++)
                {
                    try
                    {
                        outputHelper.WriteLine($"Have total {testProcess.Modules.Count} modules loaded");
                        break;
                    }
                    catch (Win32Exception)
                    {
                    }
                }
            }

            // Block until we see the IPC channel created, or until timeout specified.
            Task monitorSocketTask = Task.Run(() =>
            {
                while (true)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // On Windows, named pipe connection will block until the named pipe is ready to connect so no need to block here
                        break;
                    }
                    else
                    {
                        // On Linux, we wait until the socket is created.
                        var matchingFiles = Directory.GetFiles(Path.GetTempPath(), $"dotnet-diagnostic-{testProcess.Id}-*-socket"); // Try best match.
                        if (matchingFiles.Length > 0)
                        {
                            break;
                        }
                    }
                    Task.Delay(100);
                }
            });

            monitorSocketTask.Wait(TimeSpan.FromMilliseconds(timeoutInMS));
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
