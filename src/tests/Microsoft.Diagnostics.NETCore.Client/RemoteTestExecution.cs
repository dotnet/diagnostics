// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client.UnitTests
{
    /// <summary>
    /// Utility class to control remote test execution.
    /// </summary>
    public sealed class RemoteTestExecution : IAsyncDisposable
    {
        private readonly object _sync = new object();
        private readonly List<string> _standardOutputLines = new List<string>();
        private readonly List<string> _standardErrorLines = new List<string>();

        private TaskCompletionSource<string> _standardOutputLineSource = null;

        private Task IoReadingTask { get; }

        private ITestOutputHelper OutputHelper { get; }

        public TestRunner TestRunner { get; }

        private RemoteTestExecution(TestRunner runner, ITestOutputHelper outputHelper)
        {
            TestRunner = runner;
            OutputHelper = outputHelper;
            IoReadingTask = Task.WhenAll(
                ReadLinesAsync(runner.StandardOutput, _standardOutputLines, OnStandardOutputLine),
                ReadLinesAsync(runner.StandardError, _standardErrorLines, null));
        }

        //Very simple signals that synchronize execution between the test process and the debuggee process.

        public void SendSignal()
        {
            //We cannot use named synchronization primitives since they do not work across processes
            //on Linux. Use redirected standard input instead.
            TestRunner.StandardInput.Write('0');
            TestRunner.StandardInput.Flush();
        }

        public async Task WaitForSignalAsync()
        {
            TaskCompletionSource<string> standardOutputLineSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            lock (_sync)
            {
                _standardOutputLineSource = standardOutputLineSource;
            }

            await standardOutputLineSource.Task;
        }

        public static RemoteTestExecution StartProcess(string commandLine, ITestOutputHelper outputHelper, string reversedServerTransportName = null)
        {
            TestRunner runner = new TestRunner(commandLine, outputHelper, redirectError: true, redirectInput: true);
            if (!string.IsNullOrEmpty(reversedServerTransportName))
            {
                runner.AddReversedServer(reversedServerTransportName);
            }
            runner.Start(testProcessTimeout: 60_000);

            return new RemoteTestExecution(runner, outputHelper);
        }

        private static async Task ReadLinesAsync(StreamReader reader, List<string> lines, Action<string> callback)
        {
            try
            {
                while (true)
                {
                    // ReadLineAsync does not have cancellation
                    string line = await reader.ReadLineAsync().ConfigureAwait(false);

                    if (null == line)
                        break;

                    lines.Add(line);
                    callback?.Invoke(line);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OnStandardOutputLine(string line)
        {
            lock (_sync)
            {
                if (null != _standardOutputLineSource)
                {
                    _standardOutputLineSource.TrySetResult(line);
                    _standardOutputLineSource = null;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    await TestRunner.WaitForExitAsync(timeoutSource.Token);
                }
                catch (OperationCanceledException)
                {
                    OutputHelper.WriteLine($"[Test][P:{TestRunner.Pid}] Remote process did not exit within timeout period. Forcefully stopping process.");
                    TestRunner.Stop();
                }
                finally
                {
                    TestRunner.PrintStatus();
                }

                await IoReadingTask;

                OutputHelper.WriteLine($"Begin standard output:");
                foreach (string line in _standardOutputLines)
                {
                    OutputHelper.WriteLine($"[Test][P:{TestRunner.Pid}] {line}");
                }
                OutputHelper.WriteLine($"End standard output.");

                OutputHelper.WriteLine($"Begin standard error:");
                foreach (string line in _standardErrorLines)
                {
                    OutputHelper.WriteLine($"[Test][P:{TestRunner.Pid}] {line}");
                }
                OutputHelper.WriteLine($"End standard error.");
            }
            finally
            {
                TestRunner.Dispose();
            }
        }
    }
}
