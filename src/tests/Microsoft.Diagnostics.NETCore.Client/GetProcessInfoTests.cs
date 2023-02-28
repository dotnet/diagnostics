// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Diagnostics.CommonTestRunner;
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
    public class GetProcessInfoTests
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public GetProcessInfoTests(ITestOutputHelper outputHelper)
        {
            _output = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task BasicProcessInfoNoSuspendTest(TestConfiguration config)
        {
            return BasicProcessInfoTestCore(config, useAsync: false, suspend: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task BasicProcessInfoNoSuspendTestAsync(TestConfiguration config)
        {
            return BasicProcessInfoTestCore(config, useAsync: true, suspend: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task BasicProcessInfoSuspendTest(TestConfiguration config)
        {
            return BasicProcessInfoTestCore(config, useAsync: false, suspend: true);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task BasicProcessInfoSuspendTestAsync(TestConfiguration config)
        {
            return BasicProcessInfoTestCore(config, useAsync: true, suspend: true);
        }

        private async Task BasicProcessInfoTestCore(TestConfiguration config, bool useAsync, bool suspend)
        {
            if (config.RuntimeFrameworkVersionMajor < 5)
            {
                throw new SkipTestException("Not supported on < .NET 5.0");
            }
            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            if (suspend)
            {
                runner.SuspendDefaultDiagnosticPort();
            }
            await runner.Start(testProcessTimeout: 60_000, waitForTracee: !suspend);

            try
            {
                DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);

                // While suspended, the runtime will not provide entrypoint information.
                ProcessInfo processInfoBeforeResume = null;
                if (suspend)
                {
                    processInfoBeforeResume = await clientShim.GetProcessInfo();
                    ValidateProcessInfo(runner.Pid, processInfoBeforeResume);
                    Assert.True((config.RuntimeFrameworkVersionMajor < 8) == string.IsNullOrEmpty(processInfoBeforeResume.ManagedEntrypointAssemblyName));

                    await clientShim.ResumeRuntime();

                    await runner.WaitForTracee();
                }

                // The entrypoint information is available some short time after the runtime
                // begins to execute. Retry getting process information until entrypoint is available.
                ProcessInfo processInfo = await GetProcessInfoWithEntrypointAsync(clientShim);
                ValidateProcessInfo(runner.Pid, processInfo);

                // This is only true if targetFramework for the tracee app is greater than 
                Assert.Equal("Tracee", processInfo.ManagedEntrypointAssemblyName);

                if (suspend)
                {
                    Assert.Equal(processInfoBeforeResume.ProcessId, processInfo.ProcessId);
                    Assert.Equal(processInfoBeforeResume.RuntimeInstanceCookie, processInfo.RuntimeInstanceCookie);
                    Assert.Equal(processInfoBeforeResume.OperatingSystem, processInfo.OperatingSystem);
                    Assert.Equal(processInfoBeforeResume.ProcessArchitecture, processInfo.ProcessArchitecture);
                    Assert.Equal(processInfoBeforeResume.ClrProductVersionString, processInfo.ClrProductVersionString);
                    // Given we are in a .NET 6.0+ app, we should have ProcessInfo2 available. Pre and post pause should differ.
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Assert.Equal($"\"{runner.ExePath}\" {runner.Arguments}", processInfoBeforeResume.CommandLine);
                        Assert.Equal($"\"{runner.ExePath}\" {runner.Arguments}", processInfo.CommandLine);
                    }
                    else
                    {
                        Assert.Equal($"{runner.ExePath}", processInfoBeforeResume.CommandLine);
                        Assert.Equal($"{runner.ExePath} {runner.ManagedArguments}", processInfo.CommandLine);
                    }
                }
            }
            finally
            {
                runner.PrintStatus();
            }
        }

        /// <summary>
        /// Get process information with entrypoint information with exponential backoff on retries.
        /// </summary>
        private async Task<ProcessInfo> GetProcessInfoWithEntrypointAsync(DiagnosticsClientApiShim shim)
        {
            int retryMilliseconds = 5;
            int currentAttempt = 1;
            const int maxAttempts = 10;

            _output.WriteLine("Getting process info with entrypoint:");
            while (currentAttempt <= maxAttempts)
            {
                _output.WriteLine("- Attempt {0} of {1}.", currentAttempt, maxAttempts);

                ProcessInfo processInfo = await shim.GetProcessInfo();
                Assert.NotNull(processInfo);

                if (!string.IsNullOrEmpty(processInfo.ManagedEntrypointAssemblyName))
                {
                    _output.WriteLine("Got process info with entrypoint.");
                    return processInfo;
                }

                currentAttempt++;

                if (currentAttempt != maxAttempts)
                {
                    _output.WriteLine("  Waiting {0} ms.", retryMilliseconds);

                    await Task.Delay(retryMilliseconds);

                    retryMilliseconds = Math.Min(2 * retryMilliseconds, 500);
                }
            }

            throw new InvalidOperationException("Unable to get process info with entrypoint.");
        }

        private static void ValidateProcessInfo(int expectedProcessId, ProcessInfo processInfo)
        {
            Assert.NotNull(processInfo);
            Assert.Equal(expectedProcessId, (int)processInfo.ProcessId);
            Assert.NotNull(processInfo.CommandLine);
            Assert.NotNull(processInfo.OperatingSystem);
            Assert.NotNull(processInfo.ProcessArchitecture);
            Version clrVersion = ParseVersionRemoveLabel(processInfo.ClrProductVersionString);
            Assert.True(clrVersion >= new Version(6, 0, 0));
        }

        private static Version ParseVersionRemoveLabel(string versionString)
        {
            Assert.NotNull(versionString);
            int prereleaseLabelIndex = versionString.IndexOf('-');
            if (prereleaseLabelIndex >= 0)
            {
                versionString = versionString.Substring(0, prereleaseLabelIndex);
            }
            return Version.Parse(versionString);
        }
    }
}
