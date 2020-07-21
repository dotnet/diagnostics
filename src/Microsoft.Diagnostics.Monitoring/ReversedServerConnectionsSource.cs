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
    internal class ReversedServerConnectionsSource : IDiagnosticsConnectionsSourceInternal, IAsyncDisposable
    {
        // The amount of time to wait when checking if the a runtime instance connection should be
        // pruned from the list of connections. If the runtime doesn't have a viable connection within
        // this time, it will be pruned from the list.
        private static readonly TimeSpan PruneWaitForConnectionTimeout = TimeSpan.FromMilliseconds(250);
        // The amount of time to wait after issuing the ResumeRuntime command. If runtime instance does
        // not reconnect within this time period, the runtime connection will not be added to the list.
        private static readonly TimeSpan ResumeRuntimeTimeout = TimeSpan.FromMilliseconds(250);

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly IList<ReversedDiagnosticsConnection> _connections = new List<ReversedDiagnosticsConnection>();
        private readonly SemaphoreSlim _connectionsSemaphore = new SemaphoreSlim(1);
        private readonly string _transportPath;

        private Task _listenTask;
        private bool _disposed = false;
        private ReversedDiagnosticsServer _server;

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
            _transportPath = transportPath;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _cancellation.Cancel();

                if (null != _listenTask)
                {
                    await _listenTask;
                }

                _server?.Dispose();

                _connectionsSemaphore.Dispose();

                _cancellation.Dispose();

                _disposed = true;
            }
        }

        /// <summary>
        /// Starts listening to the reversed diagnostics server for new connections.
        /// </summary>
        public void Listen()
        {
            Listen(ReversedDiagnosticsServer.MaxAllowedConnections);
        }

        /// <summary>
        /// Starts listening to the reversed diagnostics server for new connections.
        /// </summary>
        /// <param name="maxConnections">The maximum number of connections the server will support.</param>
        public void Listen(int maxConnections)
        {
            VerifyNotDisposed();

            if (null != _server || null != _listenTask)
            {
                throw new InvalidOperationException(nameof(ReversedServerConnectionsSource.Listen) + " method can only be called once.");
            }

            _server = new ReversedDiagnosticsServer(_transportPath);

            _listenTask = ListenAsync(_cancellation.Token);
        }

        /// <summary>
        /// Gets the list of <see cref="ReversedDiagnosticsConnection"/> served from the reversed diagnostics server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A list of active <see cref="IDiagnosticsConnection"/> instances.</returns>
        public async Task<IEnumerable<IDiagnosticsConnection>> GetConnectionsAsync(CancellationToken token)
        {
            VerifyNotDisposed();

            using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);
            CancellationToken linkedToken = linkedSource.Token;

            // Prune connections that no longer have an active runtime instance before
            // returning the list of connections.
            await _connectionsSemaphore.WaitAsync(linkedToken);
            try
            {
                // Check the transport for each connection and remove the connection if the check fails.
                var connections = _connections.ToList();

                var pruneTasks = new List<Task>();
                foreach (ReversedDiagnosticsConnection connection in connections)
                {
                    pruneTasks.Add(Task.Run(() => PruneConnectionIfNotViable(connection, linkedToken), linkedToken));
                }

                await Task.WhenAll(pruneTasks);

                return _connections.Select(c => new DiagnosticsConnection(c));
            }
            finally
            {
                _connectionsSemaphore.Release();
            }
        }

        private async Task PruneConnectionIfNotViable(ReversedDiagnosticsConnection connection, CancellationToken token)
        {
            using var timeoutSource = new CancellationTokenSource();
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutSource.Token);

            try
            {
                timeoutSource.CancelAfter(PruneWaitForConnectionTimeout);

                await connection.Endpoint.WaitForConnectionAsync(linkedSource.Token);
            }
            catch
            {
                // Only remove the connection if due to some exception
                // other than cancelling the pruning operation.
                if (!token.IsCancellationRequested)
                {
                    _connections.Remove(connection);

                    OnRemovedConnection(connection);

                    // Dispose the connection to release the tracking resources in the reversed server.
                    connection.Dispose();
                }
            }
        }

        /// <summary>
        /// Accepts connections from the reversed diagnostics server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        private async Task ListenAsync(CancellationToken token)
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

                    _ = Task.Run(() => ResumeAndQueueConnection(connection, token), token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private async Task ResumeAndQueueConnection(ReversedDiagnosticsConnection connection, CancellationToken token)
        {
            try
            {
                // Send ResumeRuntime message for runtime instances that connect to the server. This will allow
                // those instances that are configured to pause on start to resume after the diagnostics
                // connection has been made. Instances that are not configured to pause on startup will ignore
                // the command and return success.
                var client = new DiagnosticsClient(connection.Endpoint);
                try
                {
                    client.ResumeRuntime();
                }
                catch (ServerErrorException)
                {
                    // The runtime likely doesn't understand the ResumeRuntime command.
                }

                // The ResumeRuntime message will consume the stream.
                // Wait until the server repopulates the stream so that the source doesn't try to offer
                // a connection that cannot be immediately used. If it is offered immediately, timing issues could ensue
                // such as when the GetConnectionsAsync call prunes connections that fail the WaitForConnectionAsync check.
                using var timeoutSource = new CancellationTokenSource();
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutSource.Token);
                timeoutSource.CancelAfter(ResumeRuntimeTimeout);

                await connection.Endpoint.WaitForConnectionAsync(linkedSource.Token);

                await _connectionsSemaphore.WaitAsync(token);
                try
                {
                    _connections.Add(connection);

                    OnNewConnection(connection);
                }
                finally
                {
                    _connectionsSemaphore.Release();
                }
            }
            catch (Exception)
            {
                // If any exceptions occur, dispose the connection so that it is removed from the server.
                connection.Dispose();

                throw;
            }
        }

        internal virtual void OnNewConnection(ReversedDiagnosticsConnection connection)
        {
        }

        internal virtual void OnRemovedConnection(ReversedDiagnosticsConnection connection)
        {
        }

        private void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ReversedDiagnosticsServer));
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
