// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.CommonTestRunner;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.Tracing;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class ReversedServerTests
    {
        // Generous timeout to allow APIs to respond on slower or more constrained machines
        private static readonly TimeSpan DefaultPositiveVerificationTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DefaultNegativeVerificationTimeout = TimeSpan.FromSeconds(2);

        private readonly ITestOutputHelper _outputHelper;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public ReversedServerTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        /// <summary>
        /// Tests that server throws appropriate exceptions when not started.
        /// </summary>
        [Fact]
        public async Task ReversedServerNoStartTest()
        {
            await using var server = CreateReversedServer(out string transportName);
            // Intentionally did not start server

            using CancellationTokenSource cancellation = new CancellationTokenSource(DefaultPositiveVerificationTimeout);

            // All API surface (except for Start) should throw InvalidOperationException
            Assert.Throws<InvalidOperationException>(
                () => server.Accept(DefaultPositiveVerificationTimeout));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => server.AcceptAsync(cancellation.Token));

            Assert.Throws<InvalidOperationException>(
                () => server.Connect(Guid.Empty, DefaultPositiveVerificationTimeout));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => server.ConnectAsync(Guid.Empty, cancellation.Token));

            Assert.Throws<InvalidOperationException>(
                () => server.RemoveConnection(Guid.Empty));

            Assert.Throws<InvalidOperationException>(
                () => server.WaitForConnection(Guid.Empty, DefaultPositiveVerificationTimeout));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => server.WaitForConnectionAsync(Guid.Empty, cancellation.Token));
        }

        /// <summary>
        /// Tests that server throws appropriate exceptions when disposed.
        /// </summary>
        [Fact]
        public async Task ReversedServerDisposeTest()
        {
            var server = CreateReversedServer(out string transportName);
            server.Start();

            using CancellationTokenSource cancellation = new CancellationTokenSource(DefaultPositiveVerificationTimeout);
            Task acceptTask = server.AcceptAsync(cancellation.Token);

            // Validate server surface throws after disposal
            await server.DisposeAsync();

            // Pending tasks should throw ObjectDisposedException
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => acceptTask);
            Assert.True(acceptTask.IsFaulted);

            Assert.Throws<ObjectDisposedException>(
                () => server.Accept(DefaultPositiveVerificationTimeout));

            // Calls after dispose should throw ObjectDisposedException
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => server.AcceptAsync(cancellation.Token));

            Assert.Throws<ObjectDisposedException>(
                () => server.RemoveConnection(Guid.Empty));
        }

        [SkippableFact]
        public async Task ReversedServerAddressInUseTest()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("Not applicable on Windows due to named pipe usage.");
            }

            await using var server = CreateReversedServer(out string transportName);

            Assert.False(File.Exists(transportName), "Unix Domain Socket should not exist yet.");

            try
            {
                // Create file to simulate that the socket is already created.
                File.Create(transportName).Dispose();

                SocketException ex = Assert.Throws<SocketException>(() => server.Start());

                int expectedErrorCode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 48 : 98; // Address already in use
                Assert.Equal(expectedErrorCode, ex.ErrorCode);
            }
            finally
            {
                try
                {
                    File.Delete(transportName);
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// Tests that <see cref="ReversedDiagnosticsServer.AcceptAsync(CancellationToken)"/> does not complete
        /// when no connections are available and that cancellation will move the returned task to the cancelled state.
        /// </summary>
        [Fact]
        public async Task ReversedServerAcceptAsyncYieldsTest()
        {
            await using var server = CreateReversedServer(out string transportName);
            server.Start();

            using var cancellationSource = new CancellationTokenSource(DefaultNegativeVerificationTimeout);

            _outputHelper.WriteLine("Waiting for connection from server.");
            Task acceptTask = server.AcceptAsync(cancellationSource.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask);
            Assert.True(acceptTask.IsCanceled);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task ReversedServerNonExistingRuntimeIdentifierTest(TestConfiguration config)
        {
            await ReversedServerNonExistingRuntimeIdentifierTestCore(config, useAsync: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task ReversedServerNonExistingRuntimeIdentifierTestAsync(TestConfiguration config)
        {
            await ReversedServerNonExistingRuntimeIdentifierTestCore(config, useAsync: true);
        }

        /// <summary>
        /// Tests that invoking server methods with non-existing runtime identifier appropriately fail.
        /// </summary>
        private async Task ReversedServerNonExistingRuntimeIdentifierTestCore(TestConfiguration config, bool useAsync)
        {
            await using var server = CreateReversedServer(out string transportName);

            var shim = new ReversedDiagnosticsServerApiShim(server, useAsync);

            server.Start();

            Guid nonExistingRuntimeId = Guid.NewGuid();

            _outputHelper.WriteLine($"Testing {nameof(ReversedDiagnosticsServer.WaitForConnectionAsync)}");
            await shim.WaitForConnection(nonExistingRuntimeId, DefaultNegativeVerificationTimeout, expectTimeout: true);

            _outputHelper.WriteLine($"Testing {nameof(ReversedDiagnosticsServer.Connect)}");
            await shim.Connect(nonExistingRuntimeId, DefaultNegativeVerificationTimeout, expectTimeout: true);

            _outputHelper.WriteLine($"Testing {nameof(ReversedDiagnosticsServer.RemoveConnection)} with previously used identifier.");
            Assert.True(server.RemoveConnection(nonExistingRuntimeId));

            _outputHelper.WriteLine($"Testing {nameof(ReversedDiagnosticsServer.RemoveConnection)} with previously unused identifier.");
            Assert.False(server.RemoveConnection(Guid.NewGuid()), "Removal of nonexisting connection should fail.");
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task ReversedServerSingleTargetMultipleUseClientTest(TestConfiguration config)
        {
            await ReversedServerSingleTargetMultipleUseClientTestCore(config, useAsync: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task ReversedServerSingleTargetMultipleUseClientTestAsync(TestConfiguration config)
        {
            await ReversedServerSingleTargetMultipleUseClientTestCore(config, useAsync: true);
        }

        /// <summary>
        /// Tests that a single client can connect to server, diagnostics can occur,
        /// and multiple use of a single DiagnosticsClient is allowed.
        /// </summary>
        /// <remarks>
        /// The multiple use of a single client is important in the reverse scenario
        /// because of how the endpoint is updated with new stream information each
        /// time the target process reconnects to the server.
        /// </remarks>
        private async Task ReversedServerSingleTargetMultipleUseClientTestCore(TestConfiguration config, bool useAsync)
        {
            if (config.RuntimeFrameworkVersionMajor < 5)
            {
                throw new SkipTestException("Not supported on < .NET 5.0");
            }
            await using var server = CreateReversedServer(out string transportName);
            server.Start();
            IpcEndpointInfo info;

            // Start client pointing to diagnostics server
            await using (TestRunner runner = await StartTracee(config, transportName))
            {
                info = await ReversedServerTests.AcceptEndpointInfo(server, useAsync);

                await VerifyEndpointInfo(runner, info, useAsync);

                // There should not be any new endpoint infos
                await VerifyNoNewEndpointInfos(server, useAsync);

                await ResumeRuntime(info, useAsync);

                await runner.WaitForTracee();

                await VerifySingleSession(info, useAsync);
            }

            // Wait some time for the process to exit
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Process exited so the endpoint should not have a valid transport anymore.
            await ReversedServerTests.VerifyWaitForConnection(info, useAsync, expectTimeout: true);

            Assert.True(server.RemoveConnection(info.RuntimeInstanceCookie), "Expected to be able to remove connection from server.");

            // There should not be any more endpoint infos
            await VerifyNoNewEndpointInfos(server, useAsync);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task ReversedServerSingleTargetExitsClientInviableTest(TestConfiguration config)
        {
            await ReversedServerSingleTargetExitsClientInviableTestCore(config, useAsync: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task ReversedServerSingleTargetExitsClientInviableTestAsync(TestConfiguration config)
        {
            await ReversedServerSingleTargetExitsClientInviableTestCore(config, useAsync: true);
        }

        /// <summary>
        /// Tests that a DiagnosticsClient is not viable after target exists.
        /// </summary>
        private async Task ReversedServerSingleTargetExitsClientInviableTestCore(TestConfiguration config, bool useAsync)
        {
            if (config.RuntimeFrameworkVersionMajor < 5)
            {
                throw new SkipTestException("Not supported on < .NET 5.0");
            }
            await using var server = CreateReversedServer(out string transportName);
            server.Start();

            // Start client pointing to diagnostics server
            IpcEndpointInfo info;
            await using (TestRunner runner = await StartTracee(config, transportName))
            {
                // Get client connection
                info = await ReversedServerTests.AcceptEndpointInfo(server, useAsync);

                await VerifyEndpointInfo(runner, info, useAsync);

                // There should not be any new endpoint infos
                await VerifyNoNewEndpointInfos(server, useAsync);

                await ResumeRuntime(info, useAsync);

                await runner.WaitForTracee();

                await ReversedServerTests.VerifyWaitForConnection(info, useAsync);
            }

            // Wait some time for the process to exit
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Process exited so the endpoint should not have a valid transport anymore.
            await ReversedServerTests.VerifyWaitForConnection(info, useAsync, expectTimeout: true);

            Assert.True(server.RemoveConnection(info.RuntimeInstanceCookie), "Expected to be able to remove connection from server.");

            // There should not be any more endpoint infos
            await VerifyNoNewEndpointInfos(server, useAsync);
        }

        /// <summary>
        /// Validates that the <see cref="ReversedDiagnosticsServer"/> does not create a new server
        /// transport during disposal.
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task ReversedServerNoCreateTransportAfterDispose(TestConfiguration config)
        {
            if (config.RuntimeFrameworkVersionMajor < 5)
            {
                throw new SkipTestException("Not supported on < .NET 5.0");
            }
            var transportCallback = new IpcServerTransportCallback();
            int transportVersion = 0;

            await using var server = CreateReversedServer(out string transportName);
            server.TransportCallback = transportCallback;
            server.Start();

            await using (TestRunner runner = await StartTracee(config, transportName))
            {
                // Get client connection
                IpcEndpointInfo info = await ReversedServerTests.AcceptEndpointInfo(server, useAsync: true);

                await VerifyEndpointInfo(runner, info, useAsync: true);

                // There should not be any new endpoint infos
                await VerifyNoNewEndpointInfos(server, useAsync: true);

                await ResumeRuntime(info, useAsync: true);

                await runner.WaitForTracee();

                await ReversedServerTests.VerifyWaitForConnection(info, useAsync: true);

                transportVersion = await transportCallback.GetStableTransportVersion();

                // Server will be disposed
            }

            // Check that the reversed server did not create a new server transport upon disposal.
            Assert.Equal(transportVersion, await transportCallback.GetStableTransportVersion());
        }

        private ReversedDiagnosticsServer CreateReversedServer(out string transportName)
        {
            transportName = DiagnosticPortsHelper.CreateServerTransportName();
            _outputHelper.WriteLine("Starting reversed server at '" + transportName + "'.");
            return new ReversedDiagnosticsServer(transportName);
        }

        private static async Task<IpcEndpointInfo> AcceptEndpointInfo(ReversedDiagnosticsServer server, bool useAsync)
        {
            var shim = new ReversedDiagnosticsServerApiShim(server, useAsync);

            return await shim.Accept(DefaultPositiveVerificationTimeout);
        }

        private async Task<TestRunner> StartTracee(TestConfiguration config, string transportName)
        {
            TestRunner runner = await TestRunner.Create(config, _outputHelper, "Tracee");
            runner.SetDiagnosticPort(transportName, suspend: true);
            await runner.Start(waitForTracee: false);
            return runner;
        }

        private static async Task VerifyWaitForConnection(IpcEndpointInfo info, bool useAsync, bool expectTimeout = false)
        {
            var shim = new IpcEndpointApiShim(info.Endpoint, useAsync);

            TimeSpan timeout = expectTimeout ? DefaultNegativeVerificationTimeout : DefaultPositiveVerificationTimeout;
            await shim.WaitForConnection(timeout, expectTimeout);
        }

        /// <summary>
        /// Checks that the accepter does not provide a new endpoint info.
        /// </summary>
        private async Task VerifyNoNewEndpointInfos(ReversedDiagnosticsServer server, bool useAsync)
        {
            _outputHelper.WriteLine("Verifying there are no more connections.");

            var shim = new ReversedDiagnosticsServerApiShim(server, useAsync);

            await shim.Accept(DefaultNegativeVerificationTimeout, expectTimeout: true);

            _outputHelper.WriteLine("Verified there are no more connections.");
        }

        /// <summary>
        /// Verifies basic information on the endpoint info and that it matches the target process from the runner.
        /// </summary>
        private async Task VerifyEndpointInfo(TestRunner runner, IpcEndpointInfo info, bool useAsync, bool expectTimeout = false)
        {
            runner.WriteLine("Verifying connection information for process");
            Assert.NotNull(runner);
            Assert.Equal(runner.Pid, info.ProcessId);
            Assert.NotEqual(Guid.Empty, info.RuntimeInstanceCookie);
            Assert.NotNull(info.Endpoint);

            await ReversedServerTests.VerifyWaitForConnection(info, useAsync, expectTimeout);

            runner.WriteLine($"Connection: {info.DebuggerDisplay}");
        }

        private async Task ResumeRuntime(IpcEndpointInfo info, bool useAsync)
        {
            var clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(info.Endpoint), useAsync);

            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Resuming runtime instance.");
            try
            {
                await clientShim.ResumeRuntime(DefaultPositiveVerificationTimeout);
                _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Resumed successfully.");
            }
            catch (ServerErrorException ex)
            {
                // Runtime likely does not understand the ResumeRuntime command.
                _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies that a client can handle multiple operations simultaneously.
        /// </summary>
        private async Task VerifySingleSession(IpcEndpointInfo info, bool useAsync)
        {
            await ReversedServerTests.VerifyWaitForConnection(info, useAsync);

            var clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(info.Endpoint), useAsync);

            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Creating session #1.");
            var providers = new List<EventPipeProvider>();
            providers.Add(new EventPipeProvider(
                "System.Runtime",
                EventLevel.Informational,
                0,
                new Dictionary<string, string>() {
                    { "EventCounterIntervalSec", "1" }
                }));
            using var session = await clientShim.StartEventPipeSession(providers);

            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Verifying session produces events.");
            await VerifyEventStreamProvidesEventsAsync(info, session, 1);

            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session verification complete.");
        }

        /// <summary>
        /// Verifies that an event stream does provide events.
        /// </summary>
        private Task VerifyEventStreamProvidesEventsAsync(IpcEndpointInfo info, EventPipeSession session, int sessionNumber)
        {
            Assert.NotNull(session);
            Assert.NotNull(session.EventStream);

            return Task.Run(async () => {
                _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Creating event source.");

                // This blocks for a while due to this bug: https://github.com/microsoft/perfview/issues/1172
                using var eventSource = new EventPipeEventSource(session.EventStream);

                _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Setup event handlers.");

                // Create task completion source that is completed when any events are provided; cancel it if cancellation is requested
                var receivedEventsSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

                using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                using var _ = cancellation.Token.Register(() => {
                    if (receivedEventsSource.TrySetCanceled())
                    {
                        _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Cancelled event processing.");
                    }
                });

                // Create continuation task that stops the session (which immediately stops event processing).
                Task stoppedProcessingTask = receivedEventsSource.Task
                    .ContinueWith(_ => {
                        _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Stopping session.");
                        session.Stop();
                    });

                // Signal task source when an event is received.
                Action<TraceEvent> allEventsHandler = _ => {
                    if (receivedEventsSource.TrySetResult(null))
                    {
                        _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Received an event and set result on completion source.");
                    }
                };

                _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Start processing events.");
                eventSource.Dynamic.All += allEventsHandler;
                eventSource.Process();
                eventSource.Dynamic.All -= allEventsHandler;
                _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Stopped processing events.");

                // Wait on the task source to verify if it ran to completion or was cancelled.
                await receivedEventsSource.Task;

                _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Waiting for session to stop.");
                await stoppedProcessingTask;
            });
        }

        private class ReversedDiagnosticsServerApiShim
        {
            private ReversedDiagnosticsServer _server;
            private readonly bool _useAsync;

            public ReversedDiagnosticsServerApiShim(ReversedDiagnosticsServer server, bool useAsync)
            {
                _server = server;
                _useAsync = useAsync;
            }

            public async Task<IpcEndpointInfo> Accept(TimeSpan timeout, bool expectTimeout = false)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    if (expectTimeout)
                    {
                        await Assert.ThrowsAsync<TaskCanceledException>(() => _server.AcceptAsync(cancellation.Token));
                        return default;
                    }
                    else
                    {
                        return await _server.AcceptAsync(cancellation.Token);
                    }
                }
                else
                {
                    if (expectTimeout)
                    {
                        Assert.Throws<TimeoutException>(() => _server.Accept(timeout));
                        return default;
                    }
                    else
                    {
                        return _server.Accept(timeout);
                    }
                }
            }

            public async Task<Stream> Connect(Guid runtimeInstanceCookie, TimeSpan timeout, bool expectTimeout = false)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    if (expectTimeout)
                    {
                        await Assert.ThrowsAsync<TaskCanceledException>(() => _server.ConnectAsync(runtimeInstanceCookie, cancellation.Token));
                        return null;
                    }
                    else
                    {
                        return await _server.ConnectAsync(runtimeInstanceCookie, cancellation.Token);
                    }
                }
                else
                {
                    if (expectTimeout)
                    {
                        Assert.Throws<TimeoutException>(() => _server.Connect(runtimeInstanceCookie, timeout));
                        return null;
                    }
                    else
                    {
                        return _server.Connect(runtimeInstanceCookie, timeout);
                    }
                }
            }

            public async Task WaitForConnection(Guid runtimeInstanceCookie, TimeSpan timeout, bool expectTimeout = false)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    if (expectTimeout)
                    {
                        await Assert.ThrowsAsync<TaskCanceledException>(() => _server.WaitForConnectionAsync(runtimeInstanceCookie, cancellation.Token));
                    }
                    else
                    {
                        await _server.WaitForConnectionAsync(runtimeInstanceCookie, cancellation.Token);
                    }
                }
                else
                {
                    if (expectTimeout)
                    {
                        Assert.Throws<TimeoutException>(() => _server.WaitForConnection(runtimeInstanceCookie, timeout));
                    }
                    else
                    {
                        _server.WaitForConnection(runtimeInstanceCookie, timeout);
                    }
                }
            }
        }

        private class IpcEndpointApiShim
        {
            private IpcEndpoint _endpoint;
            private readonly bool _useAsync;

            public IpcEndpointApiShim(IpcEndpoint endpoint, bool useAsync)
            {
                _endpoint = endpoint;
                _useAsync = useAsync;
            }

            public async Task<Stream> Connect(TimeSpan timeout, bool expectTimeout = false)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    if (expectTimeout)
                    {
                        await Assert.ThrowsAsync<TaskCanceledException>(() => _endpoint.ConnectAsync(cancellation.Token));
                        return null;
                    }
                    else
                    {
                        return await _endpoint.ConnectAsync(cancellation.Token);
                    }
                }
                else
                {
                    if (expectTimeout)
                    {
                        Assert.Throws<TimeoutException>(() => _endpoint.Connect(timeout));
                        return null;
                    }
                    else
                    {
                        return _endpoint.Connect(timeout);
                    }
                }
            }

            public async Task WaitForConnection(TimeSpan timeout, bool expectTimeout = false)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    if (expectTimeout)
                    {
                        await Assert.ThrowsAsync<TaskCanceledException>(() => _endpoint.WaitForConnectionAsync(cancellation.Token));
                    }
                    else
                    {
                        await _endpoint.WaitForConnectionAsync(cancellation.Token);
                    }
                }
                else
                {
                    if (expectTimeout)
                    {
                        Assert.Throws<TimeoutException>(() => _endpoint.WaitForConnection(timeout));
                    }
                    else
                    {
                        _endpoint.WaitForConnection(timeout);
                    }
                }
            }
        }

        private class IpcServerTransportCallback : IIpcServerTransportCallbackInternal
        {
            private static readonly TimeSpan StableTransportSemaphoreTimeout = TimeSpan.FromSeconds(3);
            private static readonly TimeSpan StableTransportVersionPeriod = TimeSpan.FromSeconds(3);
            private static readonly TimeSpan StableTransportVersionTimeout = TimeSpan.FromSeconds(30);

            private readonly Timer _transportVersionTimer;
            private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);

            private int _transportVersion;
            private TaskCompletionSource<int> _transportVersionSource;

            public IpcServerTransportCallback()
            {
                // Initially set timer to not callback
                _transportVersionTimer = new Timer(NotifyStableTransportVersion, this, Timeout.Infinite, 0);
            }

            public void CreatedNewServer(EndPoint localEp)
            {
                _semaphore.Wait(StableTransportSemaphoreTimeout);
                try
                {
                    _transportVersion++;
                    // Restart timer with existing settings
                    _transportVersionTimer.Change(0, 0);
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            private static void NotifyStableTransportVersion(object state)
            {
                ((IpcServerTransportCallback)state).NotifyStableTransportVersion();
            }

            private void NotifyStableTransportVersion()
            {
                _semaphore.Wait(StableTransportSemaphoreTimeout);
                try
                {
                    // Disable timer callback
                    _transportVersionTimer.Change(Timeout.Infinite, 0);
                    // Notify and clear the completion source
                    _transportVersionSource?.TrySetResult(_transportVersion);
                    _transportVersionSource = null;
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            public async Task<int> GetStableTransportVersion()
            {
                await _semaphore.WaitAsync(StableTransportSemaphoreTimeout);
                try
                {
                    _transportVersionSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    // Set timer to callback a period of time after last transport update
                    _transportVersionTimer.Change(StableTransportVersionPeriod, Timeout.InfiniteTimeSpan);
                }
                finally
                {
                    _semaphore.Release();
                }

                using var cancellation = new CancellationTokenSource(StableTransportVersionTimeout);

                CancellationToken token = cancellation.Token;
                using var _ = token.Register(() => _transportVersionSource.TrySetCanceled(token));

                // Wait for the transport version to stabilize for a certain amount of time.
                return await _transportVersionSource.Task;
            }
        }
    }
}
