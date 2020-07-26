// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class ReversedServerTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public ReversedServerTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        /// <summary>
        /// Tests that server throws appropriate exceptions when disposed.
        /// </summary>
        [Fact]
        public async Task ReversedServerDisposeTest()
        {
            var server = StartReversedServer(out string transportName);

            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Task acceptTask = server.AcceptAsync(cancellation.Token);

            // Validate server surface throws after disposal
            server.Dispose();

            // Pending tasks should be cancelled and throw TaskCanceledException
            await Assert.ThrowsAsync<TaskCanceledException>(() => acceptTask);
            Assert.True(acceptTask.IsCanceled);

            // Calls after dispose should throw ObjectDisposedException
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => server.AcceptAsync(cancellation.Token));

            Assert.Throws<ObjectDisposedException>(
                () => server.RemoveConnection(Guid.Empty));
        }

        /// <summary>
        /// Tests that <see cref="ReversedDiagnosticsServer.AcceptAsync(CancellationToken)"/> does not complete
        /// when no connections are available and that cancellation will move the returned task to the cancelled state.
        /// </summary>
        [Fact]
        public async Task ReversedServerAcceptAsyncYieldsTest()
        {
            using var server = StartReversedServer(out string transportName);

            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));

            _outputHelper.WriteLine("Waiting for connection from server.");
            Task acceptTask = server.AcceptAsync(cancellationSource.Token);

            await Assert.ThrowsAnyAsync<TaskCanceledException>(() => acceptTask);
            Assert.True(acceptTask.IsCanceled);
        }

        /// <summary>
        /// Tests that invoking server methods with non-existing runtime identifier appropriately fail.
        /// </summary>
        [Fact]
        public async Task ReversedServerNonExistingRuntimeIdentifierTest()
        {
            using var server = StartReversedServer(out string transportName);

            Guid nonExistingRuntimeId = Guid.NewGuid();

            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            _outputHelper.WriteLine($"Testing {nameof(ReversedDiagnosticsServer.WaitForConnectionAsync)}");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => server.WaitForConnectionAsync(nonExistingRuntimeId, cancellation.Token));

            _outputHelper.WriteLine($"Testing {nameof(ReversedDiagnosticsServer.Connect)}");
            Assert.Throws<InvalidOperationException>(
                () => server.Connect(nonExistingRuntimeId, TimeSpan.FromSeconds(3)));

            _outputHelper.WriteLine($"Testing {nameof(ReversedDiagnosticsServer.RemoveConnection)}");
            Assert.False(server.RemoveConnection(nonExistingRuntimeId), "Removal of nonexisting connection should fail.");
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
        [Fact]
        public async Task ReversedServerSingleTargetMultipleUseClientTest()
        {
            using var server = StartReversedServer(out string transportName);
            await using var accepter = new ConnectionAccepter(server, _outputHelper);

            TestRunner runner = null;
            IpcEndpointInfo info;
            try
            {
                // Start client pointing to diagnostics server
                runner = StartTracee(transportName);

                info = await AcceptAsync(accepter);

                await VerifyConnection(runner, info);

                // There should not be any more available connections
                await VerifyNoMoreConnections(accepter);

                await VerifyMultipleSessions(info);
            }
            finally
            {
                _outputHelper.WriteLine("Stopping tracee.");
                runner?.Stop();
            }

            // Wait some time for the process to exit
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Process exited so the endpoint should not have a valid transport anymore.
            await VerifyWaitForConnection(info, expectAvailableConnection: false);

            Assert.True(server.RemoveConnection(info.RuntimeInstanceCookie), "Expected to be able to remove connection from server.");

            // There should not be any more available connections
            await VerifyNoMoreConnections(accepter);
        }

        /// <summary>
        /// Tests that a DiagnosticsClient is not viable after target exists.
        /// </summary>
        [Fact]
        public async Task ReversedServerSingleTargetExitsClientInviableTest()
        {
            using var server = StartReversedServer(out string transportName);
            await using var accepter = new ConnectionAccepter(server, _outputHelper);

            TestRunner runner = null;
            DiagnosticsClient client = null;
            IpcEndpointInfo info;
            try
            {
                // Start client pointing to diagnostics server
                runner = StartTracee(transportName);

                // Get client connection
                info = await AcceptAsync(accepter);

                await VerifyConnection(runner, info);

                // There should not be any more available connections
                await VerifyNoMoreConnections(accepter);

                client = new DiagnosticsClient(info.Endpoint);

                ResumeRuntime(info, client);

                await VerifyWaitForConnection(client);
            }
            finally
            {
                _outputHelper.WriteLine("Stopping tracee.");
                runner?.Stop();
            }

            // Wait some time for the process to exit
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Process exited so the endpoint should not have a valid transport anymore.
            await VerifyWaitForConnection(client, expectConnection: false);

            Assert.True(server.RemoveConnection(info.RuntimeInstanceCookie), "Expected to be able to remove connection from server.");

            // There should not be any more available connections
            await VerifyNoMoreConnections(accepter);
        }

        /// <summary>
        /// Tests that more than one target can connect to the diagnostics server and
        /// have requests handled for each target simultaneously.
        /// </summary>
        [Fact]
        public async Task ReversedServerMultipleTargetsMultipleUseClientTest()
        {
            using var server = StartReversedServer(out string transportName);
            await using var accepter = new ConnectionAccepter(server, _outputHelper);

            IpcEndpointInfo info1;
            IpcEndpointInfo info2;
            TestRunner runner1 = null;
            TestRunner runner2 = null;
            try
            {
                // Start clients pointing to diagnostics server
                runner1 = StartTracee(transportName);
                info1 = await AcceptAsync(accepter);
                await VerifyConnection(runner1, info1);

                runner2 = StartTracee(transportName);
                info2 = await AcceptAsync(accepter);
                await VerifyConnection(runner2, info2);

                // Verify that the connections are different
                Assert.NotEqual(info1.ProcessId, info2.ProcessId);
                Assert.NotEqual(info1.RuntimeInstanceCookie, info2.RuntimeInstanceCookie);
                Assert.NotEqual(info1.Endpoint, info2.Endpoint);

                // There should not be any more available connections
                await VerifyNoMoreConnections(accepter);

                Task target1VerificationTask = VerifyMultipleSessions(info1);
                Task target2VerificationTask = VerifyMultipleSessions(info2);

                // Allow target verifications to run in parallel
                await Task.WhenAll(target1VerificationTask, target2VerificationTask);

                // Check status of each verification task
                await target1VerificationTask;
                await target2VerificationTask;
            }
            finally
            {
                _outputHelper.WriteLine("Stopping tracees.");
                runner1?.Stop();
                runner2?.Stop();
            }

            // Wait some time for the processes to exit
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Processes exited so the endpoints should not have a valid transport anymore.
            await VerifyWaitForConnection(info1, expectAvailableConnection: false);
            await VerifyWaitForConnection(info2, expectAvailableConnection: false);

            Assert.True(server.RemoveConnection(info1.RuntimeInstanceCookie), "Expected to be able to remove connection 1 from server.");
            Assert.True(server.RemoveConnection(info2.RuntimeInstanceCookie), "Expected to be able to remove connection 2 from server.");

            // There should not be any more available connections
            await VerifyNoMoreConnections(accepter);
        }

        private ReversedDiagnosticsServer StartReversedServer(out string transportName)
        {
            transportName = ReversedServerHelper.CreateServerTransportName();
            _outputHelper.WriteLine("Starting reversed server at '" + transportName + "'.");
            return new ReversedDiagnosticsServer(transportName);
        }

        private async Task<IpcEndpointInfo> AcceptAsync(ConnectionAccepter accepter)
        {
            using (var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                return await accepter.AcceptAsync(cancellationSource.Token);
            }
        }

        private TestRunner StartTracee(string transportName)
        {
            _outputHelper.WriteLine("Starting tracee.");
            return ReversedServerHelper.StartTracee(_outputHelper, transportName);
        }

        private static EventPipeProvider CreateProvider(string name)
        {
            return new EventPipeProvider(name, EventLevel.Verbose, (long)EventKeywords.All);
        }

        private async Task VerifyWaitForConnection(DiagnosticsClient client, bool expectConnection = true)
        {
            using var connectionCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            if (expectConnection)
            {
                await client.WaitForConnectionAsync(connectionCancellation.Token);
            }
            else
            {
                await Assert.ThrowsAsync<TaskCanceledException>(
                    () => client.WaitForConnectionAsync(connectionCancellation.Token));
            }
        }

        private async Task VerifyWaitForConnection(IpcEndpointInfo info, bool expectAvailableConnection = true)
        {
            using var connectionCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            if (expectAvailableConnection)
            {
                await info.Endpoint.WaitForConnectionAsync(connectionCancellation.Token);
            }
            else
            {
                await Assert.ThrowsAsync<TaskCanceledException>(
                    () => info.Endpoint.WaitForConnectionAsync(connectionCancellation.Token));
            }
        }

        /// <summary>
        /// Checks that the accepter does not provide a connection.
        /// </summary>
        private async Task VerifyNoMoreConnections(ConnectionAccepter accepter)
        {
            _outputHelper.WriteLine("Verifying there are no more connections.");

            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));

            Task acceptTask = accepter.AcceptAsync(cancellationSource.Token);
            await Assert.ThrowsAsync<OperationCanceledException>(() => acceptTask);
            Assert.True(acceptTask.IsCanceled);

            _outputHelper.WriteLine("Verified there are no more connections.");
        }

        /// <summary>
        /// Verifies basic information on the connection and that it matches the target process from the runner.
        /// </summary>
        private async Task VerifyConnection(TestRunner runner, IpcEndpointInfo info, bool expectAvailableConnection = true)
        {
            _outputHelper.WriteLine($"Verifying connection information for process ID {runner.Pid}.");
            Assert.NotNull(runner);
            Assert.Equal(runner.Pid, info.ProcessId);
            Assert.NotEqual(Guid.Empty, info.RuntimeInstanceCookie);
            Assert.NotNull(info.Endpoint);

            await VerifyWaitForConnection(info, expectAvailableConnection);

            _outputHelper.WriteLine($"Connection: {info.ToTestString()}");
        }

        private void ResumeRuntime(IpcEndpointInfo info, DiagnosticsClient client)
        {
            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Resuming runtime instance.");
            try
            {
                client.ResumeRuntime();
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
        private async Task VerifyMultipleSessions(IpcEndpointInfo info)
        {
            var client = new DiagnosticsClient(info.Endpoint);

            await VerifyWaitForConnection(client);

            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #1 - Creating session.");
            var providers1 = new List<EventPipeProvider>();
            providers1.Add(CreateProvider("Microsoft-Windows-DotNETRuntime"));
            var session1 = client.StartEventPipeSession(providers1);

            var verify1Task = Task.Run(() => VerifyEventStreamProvidesEventsAsync(info, session1, 1));

            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #2 - Creating session.");
            var providers2 = new List<EventPipeProvider>();
            providers2.Add(CreateProvider("Microsoft-DotNETCore-SampleProfiler"));
            var session2 = client.StartEventPipeSession(providers2);

            var verify2Task = Task.Run(() => VerifyEventStreamProvidesEventsAsync(info, session2, 2));

            ResumeRuntime(info, client);

            // Allow session verifications to run in parallel
            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Waiting for session verifications.");
            await Task.WhenAll(verify1Task, verify2Task);

            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Sessions finished.");
            await verify1Task;
            await verify2Task;

            // Check that sessions and streams are unique
            Assert.NotEqual(session1, session2);
            Assert.NotEqual(session1.EventStream, session2.EventStream);
            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Sessions verified.");

            session1.Dispose();
            session2.Dispose();
        }

        /// <summary>
        /// Verifies that an event stream does provide events.
        /// </summary>
        private async Task VerifyEventStreamProvidesEventsAsync(IpcEndpointInfo info, EventPipeSession session, int sessionNumber)
        {
            Assert.NotNull(session);
            Assert.NotNull(session.EventStream);

            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Checking produces events.");

            // This blocks for a while due to this bug: https://github.com/microsoft/perfview/issues/1172
            using var eventSource = new EventPipeEventSource(session.EventStream);

            // Create task completion source that is completed when any events are provided; cancel it if cancellation is requested
            var receivedEventsSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            using var _ = cancellation.Token.Register(() => receivedEventsSource.TrySetCanceled());

            // Create continuation task that stops the session (which immediately stops event processing).
            Task stoppedProcessingTask = receivedEventsSource.Task
                .ContinueWith(_ => session.Stop());

            // Signal task source when an event is received.
            Action<TraceEvent> allEventsHandler = _ =>
            {
                receivedEventsSource.TrySetResult(null);
            };

            // Start processing events.
            eventSource.Dynamic.All += allEventsHandler;
            eventSource.Process();
            eventSource.Dynamic.All -= allEventsHandler;

            // Wait on the task source to verify if it ran to completion or was cancelled.
            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Waiting to receive any events.");
            await receivedEventsSource.Task;

            _outputHelper.WriteLine($"{info.RuntimeInstanceCookie}: Session #{sessionNumber} - Waiting for session to stop.");
            await stoppedProcessingTask;
        }

        /// <summary>
        /// Helper class for consuming connections from the reverse diagnostics server.
        /// </summary>
        /// <remarks>
        /// The diagnostics server requires that something is continuously attempting to accept connnections
        /// in order to process incoming connections. This helps facilitate that continuous accepting of
        /// connectoins so the individual tests don't have to know about the behavior. 
        /// </remarks>
        private class ConnectionAccepter : IAsyncDisposable
        {
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private readonly Queue<IpcEndpointInfo> _connections = new Queue<IpcEndpointInfo>();
            private readonly SemaphoreSlim _connectionsSemaphore = new SemaphoreSlim(0);
            private readonly Task _listenTask;
            private readonly ITestOutputHelper _outputHelper;
            private readonly ReversedDiagnosticsServer _server;

            private int _acceptedCount;
            private bool _disposed;

            public ConnectionAccepter(ReversedDiagnosticsServer server, ITestOutputHelper outputHelper)
            {
                _server = server;
                _outputHelper = outputHelper;

                _listenTask = ListenAsync(_cancellation.Token);
            }

            public async ValueTask DisposeAsync()
            {
                if (!_disposed)
                {
                    _cancellation.Cancel();

                    await _listenTask;

                    _cancellation.Dispose();

                    _disposed = true;
                }
            }

            public async Task<IpcEndpointInfo> AcceptAsync(CancellationToken token)
            {
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);

                _outputHelper.WriteLine("Waiting for connection from accepter.");
                await _connectionsSemaphore.WaitAsync(linkedSource.Token);
                _outputHelper.WriteLine("Received connection from accepter.");

                return _connections.Dequeue();
            }

            /// <summary>
            /// Continuously accept connections from the reversed diagnostics server so
            /// that <see cref="ReversedDiagnosticsServer.AcceptAsync(CancellationToken)"/>
            /// is always awaited in order to to handle new runtime instance connections
            /// as well as existing runtime instance reconnections.
            /// </summary>
            private async Task ListenAsync(CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    IpcEndpointInfo info;
                    try
                    {
                        _outputHelper.WriteLine("Waiting for connection from server.");
                        info = await _server.AcceptAsync(token);

                        _acceptedCount++;
                        _outputHelper.WriteLine($"Accepted connection #{_acceptedCount} from server: {info.ToTestString()}");
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }

                    _connections.Enqueue(info);
                    _connectionsSemaphore.Release();
                }
            }
        }
    }
}
