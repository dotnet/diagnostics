// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825

namespace Microsoft.Diagnostics.NETCore.Client
{

    /// <summary>
    /// Suite of tests that test top-level commands
    /// </summary>
    public class GetPublishedProcessesTest
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public GetPublishedProcessesTest(ITestOutputHelper outputHelper)
        {
            _output = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task PublishedProcessTest1(TestConfiguration config)
        {
            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            await runner.Start();

            List<int> publishedProcesses = new List<int>(DiagnosticsClient.GetPublishedProcesses());
            foreach (int p in publishedProcesses)
            {
                runner.WriteLine($"Saw published process {p}");
            }
            Assert.Contains(publishedProcesses, p => p == runner.Pid);
            runner.WakeupTracee();
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task MultiplePublishedProcessTest(TestConfiguration config)
        {
            TestRunner[] runner = new TestRunner[3];
            int[] pids = new int[3];

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    runner[i] = await TestRunner.Create(config, _output, "Tracee");
                    await runner[i].Start();
                    pids[i] = runner[i].Pid;
                }

                List<int> publishedProcesses = new List<int>(DiagnosticsClient.GetPublishedProcesses());
                foreach (int p in publishedProcesses)
                {
                    _output.WriteLine($"[{DateTime.Now}] Saw published process {p}");
                }

                for (int i = 0; i < 3; i++)
                {
                    Assert.Contains(publishedProcesses, p => p == pids[i]);
                }

                for (int i = 0; i < 3; i++)
                {
                    runner[i].WakeupTracee();
                }
            }
            finally
            {
                for (int i = 0; i < 3; i++)
                {
                    await runner[i].DisposeAsync();
                }
            }
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task WaitForConnectionTest(TestConfiguration config)
        {
            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            await runner.Start();

            var client = new DiagnosticsClient(runner.Pid);
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            try
            {
                await client.WaitForConnectionAsync(timeoutSource.Token);
            }
            finally
            {
                runner.WakeupTracee();
            }
        }
    }
}
