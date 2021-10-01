// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class GetProcessInfoTests
    {
        private readonly ITestOutputHelper _output;

        public GetProcessInfoTests(ITestOutputHelper outputHelper)
        {
            _output = outputHelper;
        }

        [Fact]
        public Task BasicProcessInfoNoSuspendTest()
        {
            return BasicProcessInfoTestCore(useAsync: false, suspend: false);
        }

        [Fact]
        public Task BasicProcessInfoNoSuspendTestAsync()
        {
            return BasicProcessInfoTestCore(useAsync: true, suspend: false);
        }

        [Fact]
        public Task BasicProcessInfoSuspendTest()
        {
            return BasicProcessInfoTestCore(useAsync: false, suspend: true);
        }

        [Fact]
        public Task BasicProcessInfoSuspendTestAsync()
        {
            return BasicProcessInfoTestCore(useAsync: true, suspend: true);
        }

        private async Task BasicProcessInfoTestCore(bool useAsync, bool suspend)
        {
            const string targetFramework = "net5.0";

            using TestRunner runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(targetFramework: targetFramework), _output);
            if (suspend)
            {
                runner.SuspendDefaultDiagnosticPort();
            }
            runner.Start();

            try
            {
                DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);

                // While suspended, the runtime will not provide entrypoint information.
                ProcessInfo processInfoBeforeResume = null;
                if (suspend)
                {
                    processInfoBeforeResume = await clientShim.GetProcessInfo();
                    ValidateProcessInfo(runner.Pid, processInfoBeforeResume);
                    Assert.True(string.IsNullOrEmpty(processInfoBeforeResume.ManagedEntrypointAssemblyName));

                    await clientShim.ResumeRuntime();
                }

                // The entrypoint information is available some short time after the runtime
                // begins to execute. Retry getting process information until entrypoint is available.
                _output.WriteLine("Validate entrypoint assembly is available.");
                Func<ProcessInfo, bool> entrypointNotEmptyFunc =
                    info => !string.IsNullOrEmpty(info.ManagedEntrypointAssemblyName);
                ProcessInfo processInfo = await GetProcessInfoWithCondition(clientShim, entrypointNotEmptyFunc);
                ValidateProcessInfo(runner.Pid, processInfo);
                Assert.Equal("Tracee", processInfo.ManagedEntrypointAssemblyName);

                // Validate values before resume (except for entrypoint) are the same after resume.
                if (suspend)
                {
                    Assert.Equal(processInfoBeforeResume.ProcessId, processInfo.ProcessId);
                    Assert.Equal(processInfoBeforeResume.RuntimeInstanceCookie, processInfo.RuntimeInstanceCookie);
                    Assert.Equal(processInfoBeforeResume.CommandLine, processInfo.CommandLine);
                    Assert.Equal(processInfoBeforeResume.OperatingSystem, processInfo.OperatingSystem);
                    Assert.Equal(processInfoBeforeResume.ProcessArchitecture, processInfo.ProcessArchitecture);
                    Assert.Equal(processInfoBeforeResume.ClrProductVersionString, processInfo.ClrProductVersionString);
                }

                string hostName = Path.GetFileName(CommonHelper.HostExe);
                Func<ProcessInfo, bool> cmdLineContainsHostFunc =
                    info => !string.IsNullOrEmpty(info.CommandLine) &&
                        info.CommandLine.Contains(hostName, StringComparison.OrdinalIgnoreCase);

                _output.WriteLine("Validate command line contains host.");
                Assert.NotNull(await GetProcessInfoWithCondition(clientShim, cmdLineContainsHostFunc));

                string traceeName = Path.GetFileName(CommonHelper.GetTraceePath(Assembly.GetExecutingAssembly(), targetFramework: targetFramework));
                Func<ProcessInfo, bool> cmdLineContainsTraceeFunc =
                    info => !string.IsNullOrEmpty(info.CommandLine) &&
                        info.CommandLine.Contains(traceeName, StringComparison.OrdinalIgnoreCase);

                _output.WriteLine("Validate command line contains tracee.");
                Assert.NotNull(await GetProcessInfoWithCondition(clientShim, cmdLineContainsTraceeFunc));
            }
            finally
            {
                runner.PrintStatus();
            }
        }

        /// <summary>
        /// Get process information with entrypoint information with exponential backoff on retries.
        /// </summary>
        private async Task<ProcessInfo> GetProcessInfoWithCondition(DiagnosticsClientApiShim shim, Func<ProcessInfo, bool> condition)
        {
            int retryMilliseconds = 5;
            int currentAttempt = 1;
            const int maxAttempts = 20;

            _output.WriteLine("Getting process info:");
            while (currentAttempt <= maxAttempts)
            {
                _output.WriteLine("- Attempt {0} of {1}.", currentAttempt, maxAttempts);

                ProcessInfo processInfo = await shim.GetProcessInfo();
                Assert.NotNull(processInfo);

                if (condition(processInfo))
                {
                    _output.WriteLine("Got process info.");
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

            throw new InvalidOperationException("Unable to get process info with specified condition.");
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
