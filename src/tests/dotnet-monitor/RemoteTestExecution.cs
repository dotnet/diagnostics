// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.RemoteExecutor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace DotnetMonitor.UnitTests
{
    /// <summary>
    /// Utility class to control remote test execution.
    /// </summary>
    internal sealed class RemoteTestExecution : IDisposable
    {
        public RemoteInvokeHandle RemoteProcess { get; internal set; }

        public void Start()
        {
            SendSignal();
        }

        private void SendSignal()
        {
            //We cannot use named synchronization primitives since they do not work across processes
            //on Linux. Use redirected standard input instead.
            RemoteProcess.Process.StandardInput.Write('0');
            RemoteProcess.Process.StandardInput.Flush();
        }


        public void Dispose()
        {
            SendSignal();

            RemoteProcess.Process.WaitForExit(30_000);
            RemoteProcess.Dispose();
        }
    }

    /// <summary>
    /// Utility class to execute tests remotely and write messages to the EventPipe.
    /// </summary>
    internal abstract class RemoteTest
    {
        public static RemoteTestExecution StartRemoteProcess(Func<string, int> testEntry, string loggerCategory, ITestOutputHelper outputHelper)
        {
            var options = new RemoteInvokeOptions()
            {
                Start = true,
                ExpectedExitCode = 0,
                CheckExitCode = true,
                StartInfo = new ProcessStartInfo {  RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = true},
            };

            var testExecution = new RemoteTestExecution();

            //Note lambdas may not work here since closures cannot be properly serialized across process boundaries.
            testExecution.RemoteProcess = RemoteExecutor.Invoke(testEntry, loggerCategory, options);

            try
            {
                Task<string> stdOutputTask = testExecution.RemoteProcess.Process.StandardOutput.ReadToEndAsync();
                Task<string> stdErrorTask = testExecution.RemoteProcess.Process.StandardError.ReadToEndAsync();

                Task.Run( async () =>
                {
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
                });
            }
            catch (ObjectDisposedException)
            {
                outputHelper.WriteLine("Failed to collect remote process's output");
            }

            return testExecution;
        }

        public int TestBody(string loggerCategory)
        {
            Console.WriteLine("Starting remote test process");

            ServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder =>
            {
                builder.AddEventSourceLogger();
            });

            using var loggerFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(loggerCategory);

            Console.WriteLine($"{DateTime.UtcNow} Awaiting start");
            if (Console.Read() == -1)
            {
                throw new InvalidOperationException("Unable to receive start signal");
            }

            Console.WriteLine($"{DateTime.UtcNow} Starting test body");
            TestBodyCore(logger);

            Console.WriteLine($"{DateTime.UtcNow} Awaiting end");
            if (Console.Read() == -1)
            {
                throw new InvalidOperationException("Unable to receive end signal");
            }


            Console.WriteLine($"{DateTime.UtcNow} Ending remote test process");

            return 0;
        }

        protected abstract void TestBodyCore(ILogger logger);
    }
}
