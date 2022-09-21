// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client.UnitTests
{
    /// <summary>
    /// Utility class to control remote test execution.
    /// </summary>
    public sealed class RemoteTestExecution : IAsyncDisposable
    {
        private Task IoReadingTask { get; }

        private ITestOutputHelper OutputHelper { get; }

        public TestRunner TestRunner { get; }

        private RemoteTestExecution(TestRunner runner, Task ioReadingTask, ITestOutputHelper outputHelper)
        {
            TestRunner = runner;
            IoReadingTask = ioReadingTask;
            OutputHelper = outputHelper;
        }

        //Very simple signals that synchronize execution between the test process and the debuggee process.

        public void SendSignal()
        {
            //We cannot use named synchronization primitives since they do not work across processes
            //on Linux. Use redirected standard input instead.
            TestRunner.StandardInput.Write('0');
            TestRunner.StandardInput.Flush();
        }

        public void WaitForSignal()
        {
            var result = TestRunner.StandardOutput.ReadLine();
            if (string.Equals(result, "1"))
            {
                return;
            }
        }

        public static RemoteTestExecution StartProcess(string commandLine, ITestOutputHelper outputHelper, string reversedServerTransportName = null)
        {
            TestRunner runner = new TestRunner(commandLine, outputHelper, redirectError: true, redirectInput: true);
            if (!string.IsNullOrEmpty(reversedServerTransportName))
            {
                runner.SetDiagnosticPort(reversedServerTransportName, suspend: false);
            }
            runner.Start(testProcessTimeout: 60_000);

            Task readingTask = ReadAllOutput(runner.StandardOutput, runner.StandardError, outputHelper);

            return new RemoteTestExecution(runner, readingTask, outputHelper);
        }

        private static Task ReadAllOutput(StreamReader output, StreamReader error, ITestOutputHelper outputHelper)
        {
            return Task.Run(async () =>
            {
                try
                {
                    Task<string> stdErrorTask = error.ReadToEndAsync();

                    try
                    {
                        string result = await stdErrorTask;
                        outputHelper.WriteLine("Stderr:");
                        if (result != null)
                        {
                            outputHelper.WriteLine(result);
                        }
                    }
                    catch (Exception e)
                    {
                        outputHelper.WriteLine("Error reading standard error from child process: " + e.ToString());
                    }
                }
                catch (ObjectDisposedException)
                {
                    outputHelper.WriteLine("Failed to collect remote process's output");
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                await TestRunner.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                OutputHelper.WriteLine("Remote process did not exit within timeout period. Forcefully stopping process.");
                TestRunner.Stop();
            }

            await IoReadingTask;
        }
    }
}
