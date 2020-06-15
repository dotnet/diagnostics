// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// Establishes server endpoint for processes to connect when configured to provide diagnostics connection is reverse mode.
    /// </summary>
    internal sealed class ReversedDiagnosticsServer : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly ConcurrentDictionary<Guid, ServerIpcEndpoint> _endpoints = new ConcurrentDictionary<Guid, ServerIpcEndpoint>();
        private readonly IpcServerTransport _transport;

        private bool _disposed = false;

        /// <summary>
        /// Constructs the <see cref="ReversedDiagnosticsServer"/> instance with an endpoint bound
        /// to the location specified by <paramref name="transportPath"/>.
        /// </summary>
        /// <param name="transportPath">
        /// The path of the server endpoint.
        /// On Windows, this can be a full pipe path or the name without the "\\.\pipe\" prefix.
        /// On all other systems, this must be the full file path of the socket.
        /// </param>
        public ReversedDiagnosticsServer(string transportPath)
        {
            _transport = IpcServerTransport.Create(transportPath);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellation.Cancel();

                IEnumerable<ServerIpcEndpoint> endpoints = _endpoints.Values;
                _endpoints.Clear();

                foreach (var endpoint in endpoints)
                {
                    endpoint.Dispose();
                }

                _transport.Dispose();

                _cancellation.Dispose();

                _disposed = true;
            }
        }

        /// <summary>
        /// Provides connection information when a new runtime instance connects to the server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="ReversedDiagnosticsConnection"/> that contains information about the new runtime instance connection.</returns>
        /// <remarks>
        /// This will only provide connection information on the first time a runtime connects to the server. Subsequent
        /// reconects will update the existing <see cref="ReversedDiagnosticsConnection"/> instance. If a connection is removed
        /// using <see cref="RemoveConnection(Guid)"/> and the same runtime instance reconnects afte this call, then a
        /// new <see cref="ReversedDiagnosticsConnection"/> will be produced.
        /// </remarks>
        public async Task<ReversedDiagnosticsConnection> AcceptAsync(CancellationToken token)
        {
            VerifyNotDisposed();

            using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);

            ReversedDiagnosticsConnection newConnection = null;
            do
            {
                Stream stream = await _transport.AcceptAsync(linkedSource.Token);

                linkedSource.Token.ThrowIfCancellationRequested();

                IpcAdvertise advertise = IpcAdvertise.Parse(stream);
                Guid runtimeCookie = advertise.RuntimeInstanceCookie;
                int pid = unchecked((int)advertise.ProcessId);

                // If this runtime instance already exists, update the existing connection with the new endpoint.
                // Consumers should hold onto the connection instance and use it for diagnostic communication,
                // regardless of the number of times the same runtime instance connects. This requires consumers
                // to continuously invoke the AcceptAsync method in order to handle runtime instance reconnects,
                // even if the consumer only wants to handle a single connection.
                ServerIpcEndpoint endpoint = null;
                if (!_endpoints.TryGetValue(runtimeCookie, out endpoint))
                {
                    // Create a new endpoint and connection that are cached an returned from this method.
                    endpoint = new ServerIpcEndpoint();
                    newConnection = new ReversedDiagnosticsConnection(this, endpoint, pid, runtimeCookie);

                    _endpoints.TryAdd(runtimeCookie, endpoint);
                }

                endpoint.SetStream(stream);
            }
            while (null == newConnection);

            return newConnection;
        }

        /// <summary>
        /// Removes a connection from the server so that it is no longer tracked.
        /// </summary>
        /// <param name="runtimeCookie">The runtime instance cookie that corresponds to the connection to be removed.</param>
        /// <returns>True if the connection existed and was removed; otherwise false.</returns>
        internal bool RemoveConnection(Guid runtimeCookie)
        {
            VerifyNotDisposed();

            if (_endpoints.TryRemove(runtimeCookie, out ServerIpcEndpoint endpoint))
            {
                endpoint.Dispose();
                return true;
            }
            return false;
        }

        private void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ReversedDiagnosticsServer));
            }
        }
    }
}
