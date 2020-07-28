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
    /// Aggregates diagnostic endpoints that are established at a transport path via a reversed server.
    /// </summary>
    internal class ServerEndpointInfoSource : IEndpointInfoSourceInternal, IAsyncDisposable
    {
        // The amount of time to wait when checking if the a runtime instance connection should be
        // pruned from the list of connections. If the runtime doesn't have a viable connection within
        // this time, it will be pruned from the list.
        private static readonly TimeSpan PruneWaitForConnectionTimeout = TimeSpan.FromMilliseconds(250);

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly IList<IpcEndpointInfo> _connections = new List<IpcEndpointInfo>();
        private readonly SemaphoreSlim _connectionsSemaphore = new SemaphoreSlim(1);
        private readonly string _transportPath;

        private Task _listenTask;
        private bool _disposed = false;
        private ReversedDiagnosticsServer _server;

        /// <summary>
        /// Constructs a <see cref="ServerEndpointInfoSource"/> that aggreates diagnostic endpoints
        /// from a reversed diagnostics server at path specified by <paramref name="transportPath"/>.
        /// </summary>
        /// <param name="transportPath">
        /// The path of the server endpoint.
        /// On Windows, this can be a full pipe path or the name without the "\\.\pipe\" prefix.
        /// On all other systems, this must be the full file path of the socket.
        /// </param>
        public ServerEndpointInfoSource(string transportPath)
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
                    await _listenTask.ConfigureAwait(false);
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
                throw new InvalidOperationException(nameof(ServerEndpointInfoSource.Listen) + " method can only be called once.");
            }

            _server = new ReversedDiagnosticsServer(_transportPath);

            _listenTask = ListenAsync(_cancellation.Token);
        }

        /// <summary>
        /// Gets the list of <see cref="IpcEndpointInfo"/> served from the reversed diagnostics server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A list of active <see cref="IEndpointInfo"/> instances.</returns>
        public async Task<IEnumerable<IEndpointInfo>> GetConnectionsAsync(CancellationToken token)
        {
            VerifyNotDisposed();

            using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);
            CancellationToken linkedToken = linkedSource.Token;

            // Prune connections that no longer have an active runtime instance before
            // returning the list of connections.
            await _connectionsSemaphore.WaitAsync(linkedToken).ConfigureAwait(false);
            try
            {
                // Check the transport for each connection and remove the connection if the check fails.
                var connections = _connections.ToList();

                var pruneTasks = new List<Task>();
                foreach (IpcEndpointInfo info in connections)
                {
                    pruneTasks.Add(Task.Run(() => PruneConnectionIfNotViable(info, linkedToken), linkedToken));
                }

                await Task.WhenAll(pruneTasks).ConfigureAwait(false);

                return _connections.Select(c => new DiagnosticsConnection(c));
            }
            finally
            {
                _connectionsSemaphore.Release();
            }
        }

        private async Task PruneConnectionIfNotViable(IpcEndpointInfo info, CancellationToken token)
        {
            using var timeoutSource = new CancellationTokenSource();
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutSource.Token);

            try
            {
                timeoutSource.CancelAfter(PruneWaitForConnectionTimeout);

                await info.Endpoint.WaitForConnectionAsync(linkedSource.Token).ConfigureAwait(false);
            }
            catch
            {
                // Only remove the connection if due to some exception
                // other than cancelling the pruning operation.
                if (!token.IsCancellationRequested)
                {
                    _connections.Remove(info);
                    OnRemovedConnection(info);
                    _server.RemoveConnection(info.RuntimeInstanceCookie);
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
                    IpcEndpointInfo info = await _server.AcceptAsync(token).ConfigureAwait(false);

                    _ = Task.Run(() => ResumeAndQueueEndpointInfo(info, token), token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private async Task ResumeAndQueueEndpointInfo(IpcEndpointInfo info, CancellationToken token)
        {
            try
            {
                // Send ResumeRuntime message for runtime instances that connect to the server. This will allow
                // those instances that are configured to pause on start to resume after the diagnostics
                // connection has been made. Instances that are not configured to pause on startup will ignore
                // the command and return success.
                var client = new DiagnosticsClient(info.Endpoint);
                try
                {
                    client.ResumeRuntime();
                }
                catch (ServerErrorException)
                {
                    // The runtime likely doesn't understand the ResumeRuntime command.
                }

                await _connectionsSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    _connections.Add(info);

                    OnNewConnection(info);
                }
                finally
                {
                    _connectionsSemaphore.Release();
                }
            }
            catch (Exception)
            {
                _server.RemoveConnection(info.RuntimeInstanceCookie);

                throw;
            }
        }

        internal virtual void OnNewConnection(IpcEndpointInfo info)
        {
        }

        internal virtual void OnRemovedConnection(IpcEndpointInfo info)
        {
        }

        private void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ReversedDiagnosticsServer));
            }
        }

        private class DiagnosticsConnection : IEndpointInfo
        {
            private readonly IpcEndpointInfo _info;

            public DiagnosticsConnection(IpcEndpointInfo info)
            {
                _info = info;
            }

            public IpcEndpoint Endpoint => _info.Endpoint;

            public int ProcessId => _info.ProcessId;

            public Guid RuntimeInstanceCookie => _info.RuntimeInstanceCookie;
        }
    }
}
