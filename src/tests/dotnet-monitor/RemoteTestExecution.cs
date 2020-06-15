// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Xunit.Abstractions;

namespace DotnetMonitor.UnitTests
{
    /// <summary>
    /// Utility class to control remote test execution.
    /// </summary>
    internal sealed class RemoteTestExecution : IAsyncDisposable
    {
        private Task IoReadingTask { get; }

        private RemoteTestExecution(TestRunner runner, Task ioReadingTask)
        {
            TestRunner = runner;
            IoReadingTask = ioReadingTask;
        }

        public TestRunner TestRunner { get; }

        public void Start()
        {
            SendSignal();
        }

        private void SendSignal()
        {
            //We cannot use named synchronization primitives since they do not work across processes
            //on Linux. Use redirected standard input instead.
            TestRunner.StandardInput.Write('0');
            TestRunner.StandardInput.Flush();
        }

        public static RemoteTestExecution StartProcess(string commandLine, ITestOutputHelper outputHelper, string reversedServerTransportName = null)
        {
            TestRunner runner = new TestRunner(commandLine, outputHelper, redirectError: true, redirectInput: true);
            if (!string.IsNullOrEmpty(reversedServerTransportName))
            {
                ReversedServerHelper.AddReversedServer(runner, reversedServerTransportName);
            }
            runner.Start();

            Task readingTask = ReadAllOutput(runner.StandardOutput, runner.StandardError, outputHelper);

            return new RemoteTestExecution(runner, readingTask);
        }

        private static Task ReadAllOutput(StreamReader output, StreamReader error, ITestOutputHelper outputHelper)
        {
            return Task.Run(async () =>
            {
                try
                {
                    Task<string> stdOutputTask = output.ReadToEndAsync();
                    Task<string> stdErrorTask = error.ReadToEndAsync();

                    try
                    {
                        string result = await stdOutputTask;
                        outputHelper.WriteLine("Stdout:");
                        if (result != null)
                        {
                            outputHelper.WriteLine(result);
                        }
                        result = await stdErrorTask;
                        outputHelper.WriteLine("Stderr:");
                        if (result != null)
                        {
                            outputHelper.WriteLine(result);
                        }
                    }
                    catch (Exception e)
                    {
                        outputHelper.WriteLine(e.ToString());
                        throw;
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
            SendSignal();
            await IoReadingTask;
        }
    }
}
