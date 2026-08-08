// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task OversizeProcessInfoPayloadIsRejectedTest(TestConfiguration config)
        {
            return OversizeProcessInfoPayloadIsRejectedTestCore(config, useAsync: true);
        }

        // Constants describing the ProcessInfo3 serialization format (see ds-process-protocol.c).
        // The runtime stores the total IPC message size (header + payload) in a uint16_t, so the
        // payload must leave room for the header; the test below targets a command line whose
        // serialized payload sits at the top of that range.
        private const int IpcHeaderSize = 20;
        // version (4) + ProcessId (8) + RuntimeCookie (16) + six 4-byte string-length prefixes.
        private const int ProcessInfo3FixedPayloadBytes = 4 + 8 + 16 + (6 * 4);
        // The five non-command-line strings each contribute a 2-byte UTF-16 NUL terminator.
        private const int ProcessInfo3FixedStringCount = 5;

        /// <summary>
        /// Negative test for very large ProcessInfo3 payloads. The runtime echoes the process
        /// command line back in the ProcessInfo3 response, and the total IPC message size (header +
        /// payload) is stored in a uint16_t. This test drives the command line long enough that the
        /// serialized payload reaches the top of the representable range and verifies the runtime
        /// responds with a well-formed server error while staying alive, rather than returning a
        /// malformed response or terminating.
        /// </summary>
        private async Task OversizeProcessInfoPayloadIsRejectedTestCore(TestConfiguration config, bool useAsync)
        {
            // The behavior under test was added in the .NET 11 runtime.
            if (config.RuntimeFrameworkVersionMajor < 11)
            {
                throw new SkipTestException("Requires the .NET 11 (or newer) runtime.");
            }

            // The exact command-line length that hits the overflow window depends on the lengths of
            // the other serialized strings (OS, arch, entrypoint, runtime version, RID), which vary
            // by platform and runtime build. Launch once with a known argument to measure those, then
            // compute the precise argument length that lands the payload in the danger window.
            const int CalibrationArgLength = 2000;
            string calibrationArgument = new('a', CalibrationArgLength);

            int commandLineOffset;
            int fixedStringChars;
            await using (TestRunner calibration = await TestRunner.Create(config, _output, "Tracee", calibrationArgument))
            {
                await calibration.Start(testProcessTimeout: 60_000, waitForTracee: true);
                try
                {
                    DiagnosticsClientApiShim calibrationShim = new(new DiagnosticsClient(calibration.Pid), useAsync);
                    ProcessInfo info = await GetProcessInfoWithEntrypointAsync(calibrationShim);

                    // The runtime echoes back its full OS command line, so the difference from our
                    // argument length is the constant prefix (host + managed dll + 36-char pipe GUID).
                    commandLineOffset = info.CommandLine.Length - CalibrationArgLength;
                    fixedStringChars =
                        info.OperatingSystem.Length +
                        info.ProcessArchitecture.Length +
                        (info.ManagedEntrypointAssemblyName?.Length ?? 0) +
                        (info.ClrProductVersionString?.Length ?? 0) +
                        (info.PortableRuntimeIdentifier?.Length ?? 0);
                }
                finally
                {
                    calibration.WakeupTracee();
                    calibration.PrintStatus();
                }
            }

            // payload(cmdLen) = fixed bytes + 2*(cmdLen + 1) for the command line
            //                 + 2*fixedStringChars + 2*fixedStringCount for the other strings.
            int fixedPayloadBytes = ProcessInfo3FixedPayloadBytes + (2 * fixedStringChars) + (2 * ProcessInfo3FixedStringCount);
            int ArgumentLengthForPayload(int payloadBytes) => ((payloadBytes - fixedPayloadBytes) / 2) - 1 - commandLineOffset;

            // The danger window is payload in [UINT16_MAX - header + 1, UINT16_MAX]. Sweep argument
            // lengths whose payloads cover that window (and just past it, where the size field itself
            // truncates) so the test reliably exercises the guard regardless of small modeling drift.
            const int PayloadWindowFloor = ushort.MaxValue - IpcHeaderSize + 1; // 65516
            int firstArgLength = ArgumentLengthForPayload(PayloadWindowFloor);
            const int SweepCount = 16;

            bool observedGracefulRejection = false;
            for (int i = 0; i < SweepCount; i++)
            {
                int argLength = firstArgLength + i;
                string oversizeArgument = new('a', argLength);

                await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee", oversizeArgument);
                await runner.Start(testProcessTimeout: 60_000, waitForTracee: true);
                try
                {
                    DiagnosticsClientApiShim clientShim = new(new DiagnosticsClient(runner.Pid), useAsync);

                    try
                    {
                        // A well-formed server error (rather than a crashed/closed connection or a
                        // corrupt response that throws while parsing) is proof the runtime safely
                        // rejected the oversized response. A success here means this particular length
                        // still fit; that is fine - other lengths in the sweep cover the window.
                        await clientShim.GetProcessInfo();
                    }
                    catch (ServerErrorException)
                    {
                        observedGracefulRejection = true;
                    }

                    // The regression: the target must never crash, whatever the command-line length.
                    Assert.False(Process.GetProcessById(runner.Pid).HasExited, $"Target process crashed for argument length {argLength}.");
                }
                finally
                {
                    runner.WakeupTracee();
                    runner.PrintStatus();
                }
            }

            // Guard against the sweep silently missing the window (e.g. if the serialization format
            // changes); the test is only meaningful if it actually drove the runtime into rejection.
            Assert.True(observedGracefulRejection, "Sweep did not reach the payload-size overflow window; adjust the argument-length range.");
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
                DiagnosticsClientApiShim clientShim = new(new DiagnosticsClient(runner.Pid), useAsync);

                // While suspended, the runtime will not provide entrypoint information.
                ProcessInfo processInfoBeforeResume = null;
                if (suspend)
                {
                    // when the process is just starting up, the IPC channel may not be ready yet. We need to be prepared for the connection attempt to fail.
                    // If 100 retries over 10 seconds fail then we'll go ahead and fail the test.
                    const int retryCount = 100;
                    for (int i = 0; i < retryCount; i++)
                    {
                        try
                        {
                            processInfoBeforeResume = await clientShim.GetProcessInfo();
                            break;
                        }
                        catch (ServerNotAvailableException) when (i < retryCount-1)
                        {
                            _output.WriteLine($"Failed to connect to the IPC channel as the process is starting up. Attempt {i+1} of {retryCount}. Waiting 0.1 seconds, then retrying.");
                            await Task.Delay(100);
                        }
                    }
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
