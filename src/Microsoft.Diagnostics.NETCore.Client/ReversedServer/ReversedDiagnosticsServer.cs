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
    internal sealed class ReversedDiagnosticsServer : IDisposable
    {
        // Returns true if the handler is complete and should be removed from the list
        delegate bool StreamHandler(Guid runtimeId, ref Stream stream);

        // The amount of time to allow parsing of the advertise data before cancelling. This allows the server to
        // remain responsive in case the advertise data is incomplete and the stream is not closed.
        private static readonly TimeSpan ParseAdvertiseTimeout = TimeSpan.FromMilliseconds(250);

        private readonly Dictionary<Guid, ServerIpcEndpoint> _cachedEndpoints = new Dictionary<Guid, ServerIpcEndpoint>();
        private readonly Dictionary<Guid, Stream> _cachedStreams = new Dictionary<Guid, Stream>();
        private readonly CancellationTokenSource _disposalSource = new CancellationTokenSource();
        private readonly List<StreamHandler> _handlers = new List<StreamHandler>();
        private readonly object _lock = new object();
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
            : this(transportPath, MaxAllowedConnections)
        {
        }

        /// <summary>
        /// Constructs the <see cref="ReversedDiagnosticsServer"/> instance with an endpoint bound
        /// to the location specified by <paramref name="transportPath"/>.
        /// </summary>
        /// <param name="transportPath">
        /// The path of the server endpoint.
        /// On Windows, this can be a full pipe path or the name without the "\\.\pipe\" prefix.
        /// On all other systems, this must be the full file path of the socket.
        /// </param>
        /// <param name="maxConnections">The maximum number of connections the server will support.</param>
        public ReversedDiagnosticsServer(string transportPath, int maxConnections)
        {
            _transport = IpcServerTransport.Create(transportPath, maxConnections);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposalSource.Cancel();

                lock (_lock)
                {
                    _cachedEndpoints.Clear();

                    foreach (Stream stream in _cachedStreams.Values)
                    {
                        stream?.Dispose();
                    }
                    _cachedStreams.Clear();
                }

                _transport.Dispose();

                _disposalSource.Dispose();

                _disposed = true;
            }
        }

        /// <summary>
        /// Provides endpoint information when a new runtime instance connects to the server.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="IpcEndpointInfo"/> that contains information about the new runtime instance connection.</returns>
        /// <remarks>
        /// This will only provide endpoint information on the first time a runtime connects to the server.
        /// If a connection is removed using <see cref="RemoveConnection(Guid)"/> and the same runtime instance,
        /// reconnects after this call, then a new <see cref="IpcEndpointInfo"/> will be produced.
        /// </remarks>
        public async Task<IpcEndpointInfo> AcceptAsync(CancellationToken token)
        {
            VerifyNotDisposed();

            while (true)
            {
                Stream stream = null;
                IpcAdvertise advertise = null;
                try
                {
                    stream = await _transport.AcceptAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // The advertise data could be incomplete if the runtime shuts down before completely writing
                    // the information. Catch the exception and continue waiting for a new connection.
                }

                token.ThrowIfCancellationRequested();

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
                    catch (OperationCanceledException) when (parseCancellationSource.IsCancellationRequested)
                    {
                        // Only handle cancellation if it was due to the parse timeout.
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        // Catch all other exceptions and continue waiting for a new connection.
                    }
                }

                token.ThrowIfCancellationRequested();

                if (null != advertise)
                {
                    Guid runtimeCookie = advertise.RuntimeInstanceCookie;
                    int pid = unchecked((int)advertise.ProcessId);

                    lock (_lock)
                    {
                        ProvideStream(runtimeCookie, stream);
                        // Consumers should hold onto the endpoint info and use it for diagnostic communication,
                        // regardless of the number of times the same runtime instance connects. This requires consumers
                        // to continuously invoke the AcceptAsync method in order to handle runtime instance reconnects,
                        // even if the consumer only wants to handle a single endpoint.
                        if (!_cachedEndpoints.TryGetValue(runtimeCookie, out _))
                        {
                            ServerIpcEndpoint endpoint = new ServerIpcEndpoint(this, runtimeCookie);
                            _cachedEndpoints.Add(runtimeCookie, endpoint);
                            return new IpcEndpointInfo(endpoint, pid, runtimeCookie);
                        }
                    }
                }

                token.ThrowIfCancellationRequested();
            }
        }

        /// <summary>
        /// Removes endpoint information from the server so that it is no longer tracked.
        /// </summary>
        /// <param name="runtimeCookie">The runtime instance cookie that corresponds to the endpoint to be removed.</param>
        /// <returns>True if the endpoint existed and was removed; otherwise false.</returns>
        public bool RemoveConnection(Guid runtimeCookie)
        {
            VerifyNotDisposed();

            bool endpointExisted = false;
            Stream previousStream = null;

            lock (_lock)
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

        /// <remarks>
        /// This will block until the diagnostic stream is provided. This block can happen if
        /// the stream is acquired previously and the runtime instance has not yet reconnected
        /// to the reversed diagnostics server.
        /// </remarks>
        internal Stream Connect(Guid runtimeId, TimeSpan timeout)
        {
            VerifyNotDisposed();

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

            RegisterHandler(runtimeId, (Guid id, ref Stream cachedStream) =>
            {
                if (id != runtimeId)
                {
                    return false;
                }

                if (TrySetStream(StreamStateComplete, cachedStream))
                {
                    cachedStream = null;
                }

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

            var hasConnectedStreamSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var methodRegistration = token.Register(() => hasConnectedStreamSource.TrySetCanceled(token));
            using var disposalRegistration = _disposalSource.Token.Register(
                () => hasConnectedStreamSource.TrySetException(new ObjectDisposedException(nameof(ReversedDiagnosticsServer))));

            RegisterHandler(runtimeId, (Guid id, ref Stream cachedStream) =>
            {
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
                    cachedStream = null;
                    return false;
                }

                // Found a stream that is valid; signal completion if possible.
                hasConnectedStreamSource.TrySetResult(true);

                // Regardless of the registrant previously waiting or cancelled,
                // the handler should be removed from consideration.
                return true;
            });
            
            try
            {
                // Wait for the handler to verify we have a connected stream
                await hasConnectedStreamSource.Task.ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Handle all exceptions except cancellation
            }
        }

        private void ProvideStream(Guid runtimeId, Stream stream)
        {
            Debug.Assert(Monitor.IsEntered(_lock));

            // Get the previous stream in order to dispose it later
            _cachedStreams.TryGetValue(runtimeId, out Stream previousStream);

            RunStreamHandlers(runtimeId, stream);

            // Dispose the previous stream if there was one.
            previousStream?.Dispose();
        }

        private void RunStreamHandlers(Guid runtimeId, Stream stream)
        {
            Debug.Assert(Monitor.IsEntered(_lock));

            // If there are any handlers waiting for a stream, provide
            // it to the first handler in the queue.
            for (int i = 0; (i < _handlers.Count) && (null != stream); i++)
            {
                StreamHandler handler = _handlers[i];
                if (handler(runtimeId, ref stream))
                {
                    _handlers.RemoveAt(i);
                    i--;
                }
            }

            // Store the stream for when a handler registers later. If
            // a handler already captured the stream, this will be null, thus
            // representing that no existing stream is waiting to be consumed.
            _cachedStreams[runtimeId] = stream;
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

        private void RegisterHandler(Guid runtimeId, StreamHandler handler)
        {
            lock (_lock)
            {
                if (!_cachedStreams.TryGetValue(runtimeId, out Stream stream))
                {
                    throw new InvalidOperationException($"Runtime instance with identifier '{runtimeId}' is not registered.");
                }

                _handlers.Add(handler);

                if (stream != null)
                {
                    RunStreamHandlers(runtimeId, stream);
                }
            }
        }

        public static int MaxAllowedConnections = IpcServerTransport.MaxAllowedConnections;
    }
}
