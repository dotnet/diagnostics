// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        // The amount of time to wait when checking if the a endpoint info should be
        // pruned from the list of endpoint infos. If the runtime doesn't have a viable connection within
        // this time, it will be pruned from the list.
        private static readonly TimeSpan PruneWaitForConnectionTimeout = TimeSpan.FromMilliseconds(250);

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly IList<EndpointInfo> _endpointInfos = new List<EndpointInfo>();
        private readonly SemaphoreSlim _endpointInfosSemaphore = new SemaphoreSlim(1);
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
                    try
                    {
                        await _listenTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.Fail(ex.Message);
                    }
                }

                if (null != _server)
                {
                    await _server.DisposeAsync().ConfigureAwait(false);
                }

                _endpointInfosSemaphore.Dispose();

                _cancellation.Dispose();

                _disposed = true;
            }
        }

        /// <summary>
        /// Starts listening to the reversed diagnostics server for new connections.
        /// </summary>
        public void Start()
        {
            Start(ReversedDiagnosticsServer.MaxAllowedConnections);
        }

        /// <summary>
        /// Starts listening to the reversed diagnostics server for new connections.
        /// </summary>
        /// <param name="maxConnections">The maximum number of connections the server will support.</param>
        public void Start(int maxConnections)
        {
            VerifyNotDisposed();

            if (IsListening)
            {
                throw new InvalidOperationException(nameof(ServerEndpointInfoSource.Start) + " method can only be called once.");
            }

            _server = new ReversedDiagnosticsServer(_transportPath);

            _listenTask = ListenAsync(maxConnections, _cancellation.Token);
        }

        /// <summary>
        /// Gets the list of <see cref="IpcEndpointInfo"/> served from the reversed diagnostics server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A list of active <see cref="IEndpointInfo"/> instances.</returns>
        public async Task<IEnumerable<IEndpointInfo>> GetEndpointInfoAsync(CancellationToken token)
        {
            VerifyNotDisposed();

            VerifyIsListening();

            using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);
            CancellationToken linkedToken = linkedSource.Token;

            // Prune connections that no longer have an active runtime instance before
            // returning the list of connections.
            await _endpointInfosSemaphore.WaitAsync(linkedToken).ConfigureAwait(false);

            try
            {
                // Check the transport for each endpoint info and remove it if the check fails.
                IDictionary<EndpointInfo, Task<bool>> checkMap = new Dictionary<EndpointInfo, Task<bool>>();
                foreach (EndpointInfo info in _endpointInfos)
                {
                    checkMap.Add(info, Task.Run(() => CheckNotViable(info, linkedToken), linkedToken));
                }

                // Wait for all checks to complete
                await Task.WhenAll(checkMap.Values).ConfigureAwait(false);

                // Remove connections for failed checks
                foreach (KeyValuePair<EndpointInfo, Task<bool>> entry in checkMap)
                {
                    if (entry.Value.Result)
                    {
                        _endpointInfos.Remove(entry.Key);
                        OnRemovedEndpointInfo(entry.Key);
                        _server?.RemoveConnection(entry.Key.RuntimeInstanceCookie);
                    }
                }

                return _endpointInfos.ToList();
            }
            finally
            {
                _endpointInfosSemaphore.Release();
            }
        }

        /// <summary>
        /// Returns true if the connection is not longer viable.
        /// </summary>
        private static async Task<bool> CheckNotViable(EndpointInfo info, CancellationToken token)
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
                // Only report not viable if check was not cancelled.
                if (!token.IsCancellationRequested)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Accepts endpoint infos from the reversed diagnostics server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        private async Task ListenAsync(int maxConnections, CancellationToken token)
        {
            _server.Start(maxConnections);
            // Continuously accept endpoint infos from the reversed diagnostics server so
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

                EndpointInfo endpointInfo = EndpointInfo.FromIpcEndpointInfo(info);

                await _endpointInfosSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    _endpointInfos.Add(endpointInfo);

                    OnAddedEndpointInfo(endpointInfo);
                }
                finally
                {
                    _endpointInfosSemaphore.Release();
                }
            }
            catch (Exception)
            {
                _server?.RemoveConnection(info.RuntimeInstanceCookie);

                throw;
            }
        }

        internal virtual void OnAddedEndpointInfo(EndpointInfo info)
        {
        }

        internal virtual void OnRemovedEndpointInfo(EndpointInfo info)
        {
        }

        private void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ServerEndpointInfoSource));
            }
        }

        private void VerifyIsListening()
        {
            if (!IsListening)
            {
                throw new InvalidOperationException(nameof(ServerEndpointInfoSource.Start) + " method must be called before invoking this operation.");
            }
        }

        private bool IsListening => null != _server && null != _listenTask;
    }
}
