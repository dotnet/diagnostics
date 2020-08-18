// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// Establishes server endpoint for runtime instances to connect when
    /// configured to provide diagnostic endpoints in reverse mode.
    /// </summary>
    internal sealed class ReversedDiagnosticsServer : IAsyncDisposable
    {
        // Returns true if the handler is complete and should be removed from the list
        delegate bool StreamHandler(Guid runtimeId, Stream stream, out bool consumed);

        // Returns true if the handler is complete and should be removed from the list
        delegate bool EndpointInfoHandler(IpcEndpointInfo endpointInfo, out bool consumed);

        // The amount of time to allow parsing of the advertise data before cancelling. This allows the server to
        // remain responsive in case the advertise data is incomplete and the stream is not closed.
        private static readonly TimeSpan ParseAdvertiseTimeout = TimeSpan.FromMilliseconds(250);

        private readonly Dictionary<Guid, ServerIpcEndpoint> _cachedEndpoints = new Dictionary<Guid, ServerIpcEndpoint>();
        private readonly Dictionary<Guid, Stream> _cachedStreams = new Dictionary<Guid, Stream>();
        private readonly CancellationTokenSource _disposalSource = new CancellationTokenSource();
        private readonly List<EndpointInfoHandler> _newEndpointInfoHandlers = new List<EndpointInfoHandler>();
        private readonly List<IpcEndpointInfo> _newEndpointInfos = new List<IpcEndpointInfo>();
        private readonly object _newEndpointInfoLock = new object();
        private readonly List<StreamHandler> _streamHandlers = new List<StreamHandler>();
        private readonly object _streamLock = new object();
        private readonly string _transportPath;

        private bool _disposed = false;
        private Task _listenTask;

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
            _transportPath = transportPath;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposalSource.Cancel();

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

                lock (_streamLock)
                {
                    _newEndpointInfos.Clear();

                    _cachedEndpoints.Clear();

                    foreach (Stream stream in _cachedStreams.Values)
                    {
                        stream?.Dispose();
                    }
                    _cachedStreams.Clear();
                }

                _disposalSource.Dispose();

                _disposed = true;
            }
        }

        /// <summary>
        /// Starts listening at the transport path for new connections.
        /// </summary>
        public void Start()
        {
            Start(MaxAllowedConnections);
        }

        /// <summary>
        /// Starts listening at the transport path for new connections.
        /// </summary>
        /// <param name="maxConnections">The maximum number of connections the server will support.</param>
        public void Start(int maxConnections)
        {
            VerifyNotDisposed();

            if (IsStarted)
            {
                throw new InvalidOperationException(nameof(ReversedDiagnosticsServer.Start) + " method can only be called once.");
            }

            _listenTask = ListenAsync(maxConnections, _disposalSource.Token);
        }

        /// <summary>
        /// Gets endpoint information when a new runtime instance connects to the server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes with a <see cref="IpcEndpointInfo"/> value that contains information about the new runtime instance connection.</returns>
        public async Task<IpcEndpointInfo> AcceptAsync(CancellationToken token)
        {
            VerifyNotDisposed();

            VerifyIsStarted();

            var endpointInfoSource = new TaskCompletionSource<IpcEndpointInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var methodRegistration = token.Register(() => endpointInfoSource.TrySetCanceled(token));
            using var disposalRegistration = _disposalSource.Token.Register(
                () => endpointInfoSource.TrySetException(new ObjectDisposedException(nameof(ReversedDiagnosticsServer))));

            RegisterEndpointInfoHandler((IpcEndpointInfo endpointInfo, out bool consumed) =>
            {
                consumed = endpointInfoSource.TrySetResult(endpointInfo);

                // Regardless of the registrant previously waiting or cancelled,
                // the handler should be removed from consideration.
                return true;
            });

            // Wait for the handler to verify we have a connected stream
            return await endpointInfoSource.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Removes endpoint information from the server so that it is no longer tracked.
        /// </summary>
        /// <param name="runtimeCookie">The runtime instance cookie that corresponds to the endpoint to be removed.</param>
        /// <returns>True if the endpoint existed and was removed; otherwise false.</returns>
        public bool RemoveConnection(Guid runtimeCookie)
        {
            VerifyNotDisposed();

            VerifyIsStarted();

            bool endpointExisted = false;
            Stream previousStream = null;

            lock (_streamLock)
            {
                endpointExisted = _cachedEndpoints.Remove(runtimeCookie);
                if (endpointExisted)
                {
                    if (_cachedStreams.TryGetValue(runtimeCookie, out previousStream))
                    {
                        _cachedStreams.Remove(runtimeCookie);
                    }
                }
            }

            previousStream?.Dispose();

            return endpointExisted;
        }

        private void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ReversedDiagnosticsServer));
            }
        }

        private void VerifyIsStarted()
        {
            if (!IsStarted)
            {
                throw new InvalidOperationException(nameof(ReversedDiagnosticsServer.Start) + " method must be called before invoking this operation.");
            }
        }

        /// <summary>
        /// Listens at the transport path for new connections.
        /// </summary>
        /// <param name="maxConnections">The maximum number of connections the server will support.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the server is no longer listening at the transport path.</returns>
        private async Task ListenAsync(int maxConnections, CancellationToken token)
        {
            using var transport = IpcServerTransport.Create(_transportPath, maxConnections);
            while (!token.IsCancellationRequested)
            {
                Stream stream = null;
                IpcAdvertise advertise = null;
                try
                {
                    stream = await transport.AcceptAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                    // The advertise data could be incomplete if the runtime shuts down before completely writing
                    // the information. Catch the exception and continue waiting for a new connection.
                }

                if (null != stream)
                {
                    // Cancel parsing of advertise data after timeout period to
                    // mitigate runtimes that write partial data and do not close the stream (avoid waiting forever).
                    using var parseCancellationSource = new CancellationTokenSource();
                    using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, parseCancellationSource.Token);
                    try
                    {
                        parseCancellationSource.CancelAfter(ParseAdvertiseTimeout);

                        advertise = await IpcAdvertise.ParseAsync(stream, linkedSource.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                }

                if (null != advertise)
                {
                    Guid runtimeCookie = advertise.RuntimeInstanceCookie;
                    int pid = unchecked((int)advertise.ProcessId);

                    lock (_streamLock)
                    {
                        ProvideStream(runtimeCookie, stream);
                        if (!_cachedEndpoints.ContainsKey(runtimeCookie))
                        {
                            ServerIpcEndpoint endpoint = new ServerIpcEndpoint(this, runtimeCookie);
                            _cachedEndpoints.Add(runtimeCookie, endpoint);
                            ProvideEndpointInfo(new IpcEndpointInfo(endpoint, pid, runtimeCookie));
                        }
                    }
                }
            }
        }

        /// <remarks>
        /// This will block until the diagnostic stream is provided. This block can happen if
        /// the stream is acquired previously and the runtime instance has not yet reconnected
        /// to the reversed diagnostics server.
        /// </remarks>
        internal Stream Connect(Guid runtimeId, TimeSpan timeout)
        {
            VerifyNotDisposed();

            VerifyIsStarted();

            const int StreamStatePending = 0;
            const int StreamStateComplete = 1;
            const int StreamStateCancelled = 2;
            const int StreamStateDisposed = 3;

            // CancellationTokenSource is used to trigger the timeout path in order to avoid inadvertently consuming
            // the stream via the handler while processing the timeout after failing to wait for the stream event
            // to be signaled within the timeout period. The source of truth of whether the stream was consumed or
            // whether the timeout occurred is captured by the streamState variable.
            Stream stream = null;
            int streamState = StreamStatePending;
            using var streamEvent = new ManualResetEvent(false);
            var cancellationSource = new CancellationTokenSource();

            bool TrySetStream(int state, Stream value)
            {
                if (StreamStatePending == Interlocked.CompareExchange(ref streamState, state, 0))
                {
                    stream = value;
                    streamEvent.Set();
                    return true;
                }
                return false;
            }

            using var methodRegistration = cancellationSource.Token.Register(() => TrySetStream(StreamStateCancelled, value: null));
            using var disposalRegistration = _disposalSource.Token.Register(() => TrySetStream(StreamStateDisposed, value: null));

            RegisterStreamHandler(runtimeId, (Guid id, Stream cachedStream, out bool consumed) =>
            {
                consumed = false;

                if (id != runtimeId)
                {
                    return false;
                }

                consumed = TrySetStream(StreamStateComplete, cachedStream);

                // Regardless of the registrant previously waiting or cancelled,
                // the handler should be removed from consideration.
                return true;
            });

            cancellationSource.CancelAfter(timeout);
            streamEvent.WaitOne();

            if (StreamStateCancelled == streamState)
            {
                throw new TimeoutException();
            }

            if (StreamStateDisposed == streamState)
            {
                throw new ObjectDisposedException(nameof(ReversedDiagnosticsServer));
            }

            return stream;
        }

        internal async Task WaitForConnectionAsync(Guid runtimeId, CancellationToken token)
        {
            VerifyNotDisposed();

            VerifyIsStarted();

            var hasConnectedStreamSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var methodRegistration = token.Register(() => hasConnectedStreamSource.TrySetCanceled(token));
            using var disposalRegistration = _disposalSource.Token.Register(
                () => hasConnectedStreamSource.TrySetException(new ObjectDisposedException(nameof(ReversedDiagnosticsServer))));

            RegisterStreamHandler(runtimeId, (Guid id, Stream cachedStream, out bool consumed) =>
            {
                consumed = false;

                if (runtimeId != id)
                {
                    return false;
                }

                // Check if the registrant was already finished.
                if (hasConnectedStreamSource.Task.IsCompleted)
                {
                    return true;
                }

                if (!TestStream(cachedStream))
                {
                    cachedStream.Dispose();
                    consumed = true;
                    return false;
                }

                // Found a stream that is valid; signal completion if possible.
                hasConnectedStreamSource.TrySetResult(true);

                // Regardless of the registrant previously waiting or cancelled,
                // the handler should be removed from consideration.
                return true;
            });

            // Wait for the handler to verify we have a connected stream
            await hasConnectedStreamSource.Task.ConfigureAwait(false);
        }

        private void ProvideStream(Guid runtimeId, Stream stream)
        {
            Debug.Assert(Monitor.IsEntered(_streamLock));

            // Get the previous stream in order to dispose it later
            _cachedStreams.TryGetValue(runtimeId, out Stream previousStream);

            RunStreamHandlers(runtimeId, stream);

            // Dispose the previous stream if there was one.
            previousStream?.Dispose();
        }

        private void RunStreamHandlers(Guid runtimeId, Stream stream)
        {
            Debug.Assert(Monitor.IsEntered(_streamLock));

            // If there are any handlers waiting for a stream, provide
            // it to the first handler in the queue.
            bool consumedStream = false;
            for (int i = 0; !consumedStream && i < _streamHandlers.Count; i++)
            {
                StreamHandler handler = _streamHandlers[i];
                if (handler(runtimeId, stream, out consumedStream))
                {
                    _streamHandlers.RemoveAt(i);
                    i--;
                }
            }

            // Store the stream for when a handler registers later.
            _cachedStreams[runtimeId] = consumedStream ? null : stream;
        }

        private void RegisterStreamHandler(Guid runtimeId, StreamHandler handler)
        {
            lock (_streamLock)
            {
                _cachedStreams.TryGetValue(runtimeId, out Stream stream);

                _streamHandlers.Add(handler);

                if (stream != null)
                {
                    RunStreamHandlers(runtimeId, stream);
                }
            }
        }

        private bool TestStream(Stream stream)
        {
            if (null == stream)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (stream is ExposedSocketNetworkStream networkStream)
            {
                // Update Connected state of socket by sending non-blocking zero-byte data.
                Socket socket = networkStream.Socket;
                bool blocking = socket.Blocking;
                try
                {
                    socket.Blocking = false;
                    socket.Send(Array.Empty<byte>(), 0, SocketFlags.None);
                }
                catch (Exception)
                {
                }
                finally
                {
                    socket.Blocking = blocking;
                }
                return socket.Connected;
            }
            else if (stream is PipeStream pipeStream)
            {
                Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Pipe stream should only be used on Windows.");

                // PeekNamedPipe will return false if the pipe is disconnected/broken.
                return NativeMethods.PeekNamedPipe(
                    pipeStream.SafePipeHandle,
                    null,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }

            return false;
        }

        private void ProvideEndpointInfo(in IpcEndpointInfo endpointInfo)
        {
            lock (_newEndpointInfoLock)
            {
                bool consumedEndpointInfo = false;
                // Provide the endpoint info to the first handler that will accept it.
                for (int i = 0; !consumedEndpointInfo && i < _newEndpointInfoHandlers.Count; i++)
                {
                    EndpointInfoHandler handler = _newEndpointInfoHandlers[i];
                    // Handler will return true if it has completed
                    if (handler(endpointInfo, out consumedEndpointInfo))
                    {
                        _newEndpointInfoHandlers.RemoveAt(i);
                        i--;
                    }
                }

                // If the endpoint info was not consumed, add it to the list
                if (!consumedEndpointInfo)
                {
                    _newEndpointInfos.Add(endpointInfo);
                }
            }
        }

        private void RegisterEndpointInfoHandler(EndpointInfoHandler handler)
        {
            lock (_newEndpointInfoLock)
            {
                // Attempt to accept an endpoint info
                for (int i = 0; i < _newEndpointInfos.Count && null != handler; i++)
                {
                    bool consumedEnpointInfo = false;
                    // Handler will return true if it has completed
                    if (handler(_newEndpointInfos[i], out consumedEnpointInfo))
                    {
                        handler = null;
                    }

                    // If the endpoint info was consumed, remove it from the list
                    if (consumedEnpointInfo)
                    {
                        _newEndpointInfos.RemoveAt(i);
                        i--;
                    }
                }

                // If the handler did not signal completion, then add it to the handlers list.
                if (null != handler)
                {
                    _newEndpointInfoHandlers.Add(handler);
                }
            }
        }

        private bool IsStarted => null != _listenTask;

        public static int MaxAllowedConnections = IpcServerTransport.MaxAllowedConnections;
    }
}
