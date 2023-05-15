// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.CommonTestRunner;
using Microsoft.Diagnostics.TestHelpers;
using Xunit.Abstractions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace CommonTestRunner
{
    public static class TestRunnerUtilities
    {
        public static async Task<TestRunner> StartProcess(TestConfiguration config, string testArguments, ITestOutputHelper outputHelper, int testProcessTimeout = 60_000)
        {
            TestRunner runner = await TestRunner.Create(config, outputHelper, "EventPipeTracee", testArguments).ConfigureAwait(true);
            await runner.Start(testProcessTimeout).ConfigureAwait(true);
            return runner;
        }

        public static async Task ExecuteCollection(
            Func<CancellationToken, Task> executeCollection,
            TestRunner testRunner,
            CancellationToken token,
            TaskCompletionSource waitTaskSource = null)
        {
            //using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token);

            Task collectionTask = executeCollection(token);

            // Begin event production
            testRunner.WakeupTracee();

            // Wait for event production to be done
            testRunner.WaitForSignal();

            try
            {
                // Optionally wait on caller before allowing the pipeline to stop.
                if (null != waitTaskSource)
                {
                    using CancellationTokenRegistration _ = token.Register(() => {
                        testRunner.WriteLine("Did not receive completion signal before cancellation.");
                        waitTaskSource.TrySetCanceled(token);
                    });

                    await waitTaskSource.Task.ConfigureAwait(true);
                }

                //After a pipeline is stopped, we should expect the RunTask to eventually finish
                await collectionTask.ConfigureAwait(true);
            }
            finally
            {
                // Signal for debugee that's ok to end/move on.
                testRunner.WakeupTracee();
            }
        }
    }
}
