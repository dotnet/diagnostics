// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring
{
    /// <summary>
    /// Aggregates diagnostics connections that are established at a transport path via a reversed server.
    /// </summary>
    internal sealed class ReversedServerConnectionsSource : IDiagnosticsConnectionsSourceInternal, IAsyncDisposable
    {
        private readonly Task _acceptTask;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly IList<ReversedDiagnosticsConnection> _connections = new List<ReversedDiagnosticsConnection>();
        private readonly SemaphoreSlim _connectionsSemaphore = new SemaphoreSlim(1);
        private readonly ReversedDiagnosticsServer _server;

        private TaskCompletionSource<object> _newConnectionSource;

        private bool _disposed = false;

        /// <summary>
        /// Constructs a <see cref="ReversedServerConnectionsSource"/> that aggreates diagnostics connection
        /// from a reversed diagnostics server at path specified by <paramref name="transportPath"/>.
        /// </summary>
        /// <param name="transportPath">
        /// The path of the server endpoint.
        /// On Windows, this can be a full pipe path or the name without the "\\.\pipe\" prefix.
        /// On all other systems, this must be the full file path of the socket.
        /// </param>
        public ReversedServerConnectionsSource(string transportPath)
        {
            _server = new ReversedDiagnosticsServer(transportPath);

            _acceptTask = AcceptAsync(_cancellation.Token);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                _cancellation.Cancel();

                _server.Dispose();

                await _acceptTask;

                _cancellation.Dispose();

                _disposed = true;
            }
        }

        /// <summary>
        /// Gets the list of <see cref="ReversedDiagnosticsConnection"/> served from the reversed diagnostics server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A list of active <see cref="IDiagnosticsConnection"/> instances.</returns>
        public async Task<IEnumerable<IDiagnosticsConnection>> GetConnectionsAsync(CancellationToken token)
        {
            using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);
            CancellationToken linkedToken = linkedSource.Token;

            // Prune connections that no longer have an active runtime instance before
            // returning the list of connections.
            await _connectionsSemaphore.WaitAsync(linkedToken);
            try
            {
                // Create a task that checks each connection and removes it
                // if it is no longer connected or is not responsive.
                IList<Task> pruneTasks = _connections
                    .Select(c => Task.Run(() => PruneConnectionAsync(c, linkedToken), linkedToken))
                    .ToList();

                await Task.WhenAll(pruneTasks);

                return _connections
                    .Select(c => new DiagnosticsConnection(c))
                    .ToList()
                    .AsReadOnly();
            }
            finally
            {
                _connectionsSemaphore.Release();
            }
        }

        /// <summary>
        /// Accepts connections from the reversed diagnostics server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        private async Task AcceptAsync(CancellationToken token)
        {
            // Continuously accept connections from the reversed diagnostics server so
            // that <see cref="ReversedDiagnosticsServer.AcceptAsync(CancellationToken)"/>
            // is always awaited in order to to handle new runtime instance connections
            // as well as existing runtime instance reconnections.
            while (!token.IsCancellationRequested)
            {
                try
                {
                    ReversedDiagnosticsConnection connection = await _server.AcceptAsync(token);

                    await _connectionsSemaphore.WaitAsync(token);

                    _connections.Add(connection);

                    _connectionsSemaphore.Release();

                    if (null != _newConnectionSource)
                    {
                        _newConnectionSource.TrySetResult(null);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        /// <summary>
        /// Tests a diagnostics connection and removes it if the associated runtime instance is no longer active.
        /// </summary>
        private async Task PruneConnectionAsync(ReversedDiagnosticsConnection connection, CancellationToken token)
        {
            // Cancel event pipe processing within reasonable amount of time to remove potentially unresponsive processes.
            using CancellationTokenSource timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Try to get ProcessInfo information to test the connection
            try
            {
                DiagnosticsClient client = new DiagnosticsClient(connection.Endpoint);

                // NOTE: This test does not need to necessarily use the event pipe providers and collection.
                // It only needs to send some type of request and receive a response from the runtime instance
                // to verify that the diagnostics pipe/socket is viable and the runtime instance is still connected.
                await using DiagnosticsEventPipeProcessor process = new DiagnosticsEventPipeProcessor(PipeMode.ProcessInfo);

                using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutSource.Token);

                await process.Process(client, 0, Timeout.InfiniteTimeSpan, linkedSource.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Do not remove connection if method was cancelled.
                throw;
            }
            catch (Exception)
            {
                // Runtime instance likely no longer exists or is not responsive; remove connection.
                _connections.Remove(connection);

                // Dispose the connection to release the tracking resources in the reversed server.
                connection.Dispose();
            }
        }

        /// <summary>
        /// Waits for a new connection to be made available to the connection source from the reversed server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        internal async Task WaitForNewConnectionAsync(CancellationToken token)
        {
            _newConnectionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => _newConnectionSource.TrySetCanceled()))
            {
                await _newConnectionSource.Task;
            }
        }

        private class DiagnosticsConnection : IDiagnosticsConnection
        {
            private readonly ReversedDiagnosticsConnection _connection;

            public DiagnosticsConnection(ReversedDiagnosticsConnection connection)
            {
                _connection = connection;
            }

            public IIpcEndpoint Endpoint => _connection.Endpoint;

            public int ProcessId => _connection.ProcessId;

            public Guid RuntimeInstanceCookie => _connection.RuntimeInstanceCookie;
        }
    }
}
