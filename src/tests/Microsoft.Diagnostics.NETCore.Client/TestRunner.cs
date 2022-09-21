// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


using System;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class TestRunner : IDisposable
    {
        private Process testProcess;
        private ProcessStartInfo startInfo;
        private ITestOutputHelper outputHelper;
        private CancellationTokenSource cts;

        public TestRunner(string testExePath, ITestOutputHelper _outputHelper = null,
            bool redirectError = false, bool redirectInput = false, Dictionary<string, string> envVars = null)
        {
            startInfo = new ProcessStartInfo(CommonHelper.HostExe, testExePath);
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = redirectError;
            startInfo.RedirectStandardInput = redirectInput;
            envVars?.ToList().ForEach(item => startInfo.Environment.Add(item.Key, item.Value));
            outputHelper = _outputHelper;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                // Make a good will attempt to end the tracee process
                // and its process tree
                testProcess?.Kill(entireProcessTree: true);
            }
            catch {}

            if(disposing)
            {
                testProcess?.Dispose();
            }

            cts.Dispose();
        }

        public void AddEnvVar(string key, string value)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        public StreamWriter StandardInput => testProcess.StandardInput;
        public StreamReader StandardOutput => testProcess.StandardOutput;
        public StreamReader StandardError => testProcess.StandardError;

        public void Start(int timeoutInMSPipeCreation=15_000, int testProcessTimeout=30_000)
        {
            if (outputHelper != null)
                outputHelper.WriteLine($"[{DateTime.Now.ToString()}] Launching test: " + startInfo.FileName + " " + startInfo.Arguments);

            testProcess = new Process();
            testProcess.StartInfo = startInfo;
            testProcess.EnableRaisingEvents = true;

            if (!testProcess.Start())
            {
                outputHelper.WriteLine($"Could not start process: " + startInfo.FileName);
            }

            if (testProcess.HasExited)
            {
                outputHelper.WriteLine($"Process " + startInfo.FileName + " came back as exited");
            }

            cts = new CancellationTokenSource(testProcessTimeout);
            cts.Token.Register(() => testProcess.Kill());

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

            monitorSocketTask.Wait(TimeSpan.FromMilliseconds(timeoutInMSPipeCreation));
        }

        public void Stop()
        {
            this.Dispose();
        }

        public int Pid {
            get { return testProcess.Id; }
        }

        public void PrintStatus()
        {
            if (testProcess.HasExited)
            {
                outputHelper.WriteLine($"Process {testProcess.Id} status: Exited 0x{testProcess.ExitCode:X}");
            }
            else
            {
                outputHelper.WriteLine($"Process {testProcess.Id} status: Running");
            }
        }

        public async Task WaitForExitAsync(CancellationToken token)
        {
            TaskCompletionSource<object> exitedSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler exitedHandler = (s, e) => exitedSource.TrySetResult(null);

            testProcess.Exited += exitedHandler;
            try
            {
                if (!testProcess.HasExited)
                {
                    using var _ = token.Register(() => exitedSource.TrySetCanceled(token));

                    await exitedSource.Task;
                }
            }
            finally
            {
                testProcess.Exited -= exitedHandler;
            }
        }
    }
}
