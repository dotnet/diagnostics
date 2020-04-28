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
        /// <summary>
        /// These mutexes are used to coordiate the test and the remote app. The test gates the start and end of the app.
        /// TODO Mutexes are not ideal for this since they have thread affinity.
        /// </summary>
        private Mutex _startMutex;
        private Mutex _endMutex;

        public RemoteTestExecution()
        {
            _startMutex = RemoteTest.GetStartMutex(true);
            _endMutex = RemoteTest.GetEndMutex(true);
        }
        
        public RemoteInvokeHandle RemoteProcess { get; internal set; }

        public void Start()
        {
            _startMutex.ReleaseMutex();
        }

        public void Dispose()
        {
            _endMutex.ReleaseMutex();

            _startMutex.Dispose();
            _endMutex.Dispose();
        }
    }

    /// <summary>
    /// Utility class to execute tests remotely and write messages to the EventPipe.
    /// </summary>
    internal abstract class RemoteTest
    {
        internal static Mutex GetStartMutex(bool owned)
        {
            return new Mutex(owned, "TestCaseStart");
        }

        internal static Mutex GetEndMutex(bool owned)
        {
            return new Mutex(owned, "TestCaseEnd");
        }

        public static RemoteTestExecution StartRemoteProcess(Func<string, int> testEntry, string loggerCategory, ITestOutputHelper outputHelper)
        {
            var options = new RemoteInvokeOptions()
            {
                Start = true,
                ExpectedExitCode = 0,
                CheckExitCode = true,
                StartInfo = new ProcessStartInfo {  RedirectStandardError = true, RedirectStandardOutput = true},
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
                });
            }
            catch (ObjectDisposedException)
            {
                Console.Error.WriteLine("Failed to collect remote process's output");
            }

            return testExecution;
        }

        public int TestBody(string loggerCategory)
        {
            Console.WriteLine("Starting remote test process");
            
            using var startMutex = GetStartMutex(false);
            using var endMutex = GetEndMutex(false);

            ServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder =>
            {
                builder.AddEventSourceLogger();
            });

            using var loggerFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(loggerCategory);

            Console.WriteLine("Awaiting start mutex");

            if (!startMutex.WaitOne(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Mutex timed out");
            }

            Console.WriteLine("Starting test body");
            TestBodyCore(logger);

            Console.WriteLine("Awaiting end mutex");
            if (!endMutex.WaitOne(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Mutex timed out");
            }

            return 0;
        }

        protected abstract void TestBodyCore(ILogger logger);
    }
}
