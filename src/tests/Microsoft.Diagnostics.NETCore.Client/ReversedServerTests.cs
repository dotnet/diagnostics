// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dia2Lib;
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
        /// Tests that server and connections should throw ObjectDisposedException
        /// from API surface after being disposed.
        /// </summary>
        [Fact]
        public async Task ReversedServerThrowsWhenDisposedTest()
        {
            var server = StartReversedServer(out string transportName);
            await using var accepter = new ConnectionAccepter(server, _outputHelper);

            ReversedDiagnosticsConnection connection;
            TestRunner runner = null;
            try
            {
                // Start client pointing to diagnostics server
                runner = StartTracee(transportName);

                // Get client connection
                connection = await AcceptAsync(accepter);
            }
            finally
            {
                _outputHelper.WriteLine("Stopping tracee.");
                runner?.Stop();
            }

            // Wait some time for the process to exit
            await Task.Delay(TimeSpan.FromSeconds(1));

            await VerifyConnection(runner, connection, expectAvailableConnection: false);

            // Validate connection surface throws after disposal
            connection?.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => connection.Endpoint);

            Assert.Throws<ObjectDisposedException>(
                () => connection.ProcessId);

            Assert.Throws<ObjectDisposedException>(
                () => connection.RuntimeInstanceCookie);

            // Validate server surface throws after disposal
            server.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => server.AcceptAsync(CancellationToken.None));

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

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask);
            Assert.True(acceptTask.IsCanceled);
        }

        /// <summary>
        /// Tests that removing a nonexisting connection from the server should fail.
        /// </summary>
        [Fact]
        public async Task ReversedServerRemoveConnectionNonExistingTest()
        {
            using var server = StartReversedServer(out string transportName);

            await Task.Delay(TimeSpan.FromSeconds(1));

            _outputHelper.WriteLine("Removing connection.");
            Assert.False(server.RemoveConnection(Guid.NewGuid()), "Removal of nonexisting connection should fail.");
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
            ReversedDiagnosticsConnection connection = null;
            try
            {
                // Start client pointing to diagnostics server
                runner = StartTracee(transportName);

                connection = await AcceptAsync(accepter);

                await VerifyConnection(runner, connection);

                // There should not be any more available connections
                await VerifyNoMoreConnections(accepter);

                await VerifyMultipleSessions(connection);
            }
            finally
            {
                _outputHelper.WriteLine("Stopping tracee.");
                runner?.Stop();
            }

            // Wait some time for the process to exit
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Process exited so the endpoint should not have a valid transport anymore.
            await VerifyWaitForConnection(connection, expectAvailableConnection: false);

            connection?.Dispose();

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
            ReversedDiagnosticsConnection connection = null;
            try
            {
                // Start client pointing to diagnostics server
                runner = StartTracee(transportName);

                // Get client connection
                connection = await AcceptAsync(accepter);

                await VerifyConnection(runner, connection);

                // There should not be any more available connections
                await VerifyNoMoreConnections(accepter);

                client = new DiagnosticsClient(connection.Endpoint);

                ResumeRuntime(connection, client);

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

            // At this point, the target process has exited. The DiagnosticsClient should no longer be viable,
            // thus the pipe should be broken (IOException).

            var providersBrokenPipe = new List<EventPipeProvider>();
            providersBrokenPipe.Add(CreateProvider("Microsoft-Windows-DotNETRuntime"));

            EventPipeSession sessionBrokenPipe = null;
            Assert.ThrowsAny<IOException>(() =>
            {
                sessionBrokenPipe = client.StartEventPipeSession(providersBrokenPipe);
            });
            Assert.Null(sessionBrokenPipe);

            connection?.Dispose();

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

            ReversedDiagnosticsConnection connection1 = null;
            ReversedDiagnosticsConnection connection2 = null;
            TestRunner runner1 = null;
            TestRunner runner2 = null;
            try
            {
                // Start clients pointing to diagnostics server
                runner1 = StartTracee(transportName);
                connection1 = await AcceptAsync(accepter);
                await VerifyConnection(runner1, connection1);

                runner2 = StartTracee(transportName);
                connection2 = await AcceptAsync(accepter);
                await VerifyConnection(runner2, connection2);

                // Verify that the connections are different
                Assert.NotEqual(connection1.ProcessId, connection2.ProcessId);
                Assert.NotEqual(connection1.RuntimeInstanceCookie, connection2.RuntimeInstanceCookie);
                Assert.NotEqual(connection1.Endpoint, connection2.Endpoint);

                // There should not be any more available connections
                await VerifyNoMoreConnections(accepter);

                Task target1VerificationTask = VerifyMultipleSessions(connection1);
                Task target2VerificationTask = VerifyMultipleSessions(connection2);

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
            await VerifyWaitForConnection(connection1, expectAvailableConnection: false);
            await VerifyWaitForConnection(connection2, expectAvailableConnection: false);

            connection1?.Dispose();
            connection2?.Dispose();

            // There should not be any more available connections
            await VerifyNoMoreConnections(accepter);
        }

        private ReversedDiagnosticsServer StartReversedServer(out string transportName)
        {
            transportName = ReversedServerHelper.CreateServerTransportName();
            _outputHelper.WriteLine("Starting reversed server at '" + transportName + "'.");
            return new ReversedDiagnosticsServer(transportName);
        }

        private async Task<ReversedDiagnosticsConnection> AcceptAsync(ConnectionAccepter accepter, int timeoutSeconds = 1)
        {
            using (var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
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
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => client.WaitForConnectionAsync(connectionCancellation.Token));
            }
        }

        private async Task VerifyWaitForConnection(ReversedDiagnosticsConnection connection, bool expectAvailableConnection = true)
        {
            using var connectionCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            if (expectAvailableConnection)
            {
                await connection.Endpoint.WaitForConnectionAsync(connectionCancellation.Token);
            }
            else
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => connection.Endpoint.WaitForConnectionAsync(connectionCancellation.Token));
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
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask);
            Assert.True(acceptTask.IsCanceled);

            _outputHelper.WriteLine("Verified there are no more connections.");
        }

        /// <summary>
        /// Verifies basic information on the connection and that it matches the target process from the runner.
        /// </summary>
        private async Task VerifyConnection(TestRunner runner, ReversedDiagnosticsConnection connection, bool expectAvailableConnection = true)
        {
            _outputHelper.WriteLine($"Verifying connection information for process ID {runner.Pid}.");
            Assert.NotNull(runner);
            Assert.NotNull(connection);
            Assert.Equal(runner.Pid, connection.ProcessId);
            Assert.NotEqual(Guid.Empty, connection.RuntimeInstanceCookie);
            Assert.NotNull(connection.Endpoint);

            await VerifyWaitForConnection(connection, expectAvailableConnection);

            _outputHelper.WriteLine($"Connection: {connection.ToTestString()}");
        }

        private void ResumeRuntime(ReversedDiagnosticsConnection connection, DiagnosticsClient client)
        {
            _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Resuming runtime instance.");
            try
            {
                client.ResumeRuntime();
                _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Resumed successfully.");
            }
            catch (ServerErrorException ex)
            {
                // Runtime likely does not understand the ResumeRuntime command.
                _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies that a client can handle multiple operations simultaneously.
        /// </summary>
        private async Task VerifyMultipleSessions(ReversedDiagnosticsConnection connection)
        {
            var client = new DiagnosticsClient(connection.Endpoint);

            await VerifyWaitForConnection(client);

            _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Session #1 - Creating session.");
            var providers1 = new List<EventPipeProvider>();
            providers1.Add(CreateProvider("Microsoft-Windows-DotNETRuntime"));
            var session1 = client.StartEventPipeSession(providers1);

            var verify1Task = Task.Run(() => VerifyEventStreamProvidesEventsAsync(connection, session1, 1));

            _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Session #2 - Creating session.");
            var providers2 = new List<EventPipeProvider>();
            providers2.Add(CreateProvider("Microsoft-DotNETCore-SampleProfiler"));
            var session2 = client.StartEventPipeSession(providers2);

            var verify2Task = Task.Run(() => VerifyEventStreamProvidesEventsAsync(connection, session2, 2));

            ResumeRuntime(connection, client);

            // Allow session verifications to run in parallel
            _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Waiting for session verifications.");
            await Task.WhenAll(verify1Task, verify2Task);

            _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Sessions finished.");
            await verify1Task;
            await verify2Task;

            // Check that sessions and streams are unique
            Assert.NotEqual(session1, session2);
            Assert.NotEqual(session1.EventStream, session2.EventStream);
            _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Sessions verified.");

            session1.Dispose();
            session2.Dispose();
        }

        /// <summary>
        /// Verifies that an event stream does provide events.
        /// </summary>
        private async Task VerifyEventStreamProvidesEventsAsync(ReversedDiagnosticsConnection connection, EventPipeSession session, int sessionNumber)
        {
            Assert.NotNull(session);
            Assert.NotNull(session.EventStream);

            _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Session #{sessionNumber} - Checking produces events.");

            // This blocks for a while due to this bug: https://github.com/microsoft/perfview/issues/1172
            using var eventSource = new EventPipeEventSource(session.EventStream);

            // Create task completion source that is completed when any events are provided; cancel it if cancellation is requested
            var receivedEventsSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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
            _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Session #{sessionNumber} - Waiting to receive any events.");
            await receivedEventsSource.Task;

            _outputHelper.WriteLine($"{connection.RuntimeInstanceCookie}: Session #{sessionNumber} - Waiting for session to stop.");
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
            private readonly Queue<ReversedDiagnosticsConnection> _connections = new Queue<ReversedDiagnosticsConnection>();
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

            public async Task<ReversedDiagnosticsConnection> AcceptAsync(CancellationToken token)
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
                    ReversedDiagnosticsConnection connection;
                    try
                    {
                        _outputHelper.WriteLine("Waiting for connection from server.");
                        connection = await _server.AcceptAsync(token);

                        _acceptedCount++;
                        _outputHelper.WriteLine($"Accepted connection #{_acceptedCount} from server: {connection.ToTestString()}");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    _connections.Enqueue(connection);
                    _connectionsSemaphore.Release();
                }
            }
        }
    }
}
