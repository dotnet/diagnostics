// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// An endpoint used to acquire the diagnostics stream for a runtime instance.
    /// </summary>
    internal interface IIpcEndpoint
    {
        /// <summary>
        /// Wait for an available diagnostics connection to the runtime instance.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task the completes when a diagnostics connection to the runtime instance becomes available.
        /// </returns>
        Task WaitForConnectionAsync(CancellationToken token);

        /// <summary>
        /// Connects to the underlying IPC Transport and opens a read/write-able Stream
        /// </summary>
        /// <returns>A Stream for writing and reading data to and from the target .NET process</returns>
        Stream Connect();
    }

    internal abstract class BaseIpcEndpoint : IIpcEndpoint
    {
        private readonly Queue<StreamTarget> _targets = new Queue<StreamTarget>();

        private bool _disposed;
        private Stream _stream;

        /// <inheritdoc cref="IIpcEndpoint.Connect"/>
        /// <remarks>
        /// This will block until the diagnostic stream is provided. This block can happen if
        /// the stream is acquired previously and the runtime instance has not yet reconnected
        /// to the reversed diagnostics server.
        /// </remarks>
        public Stream Connect()
        {
            using var target = new StreamEventTarget();

            RegisterTarget(target);

            target.Handle.WaitOne();

            return target.Stream;
        }

        /// <inheritdoc cref="IIpcEndpoint.WaitForConnectionAsync(CancellationToken)"/>
        public async Task WaitForConnectionAsync(CancellationToken token)
        {
            bool isConnected = false;
            do
            {
                using (var target = new StreamTaskTarget(token))
                {
                    Stream stream = null;
                    try
                    {
                        RegisterTarget(target);

                        // Wait for a stream to be provided by the target
                        stream = await target.Task;

                        // Test if the stream is viable
                        isConnected = TestStream(stream);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        // Handle all exceptions except cancellation
                    }
                    finally
                    {
                        // If the stream is not connected, it can be disposed;
                        // otherwise, make the stream available again since waiting
                        // for the stream to connect should not consume the stream.
                        if (!isConnected)
                        {
                            stream?.Dispose();
                        }
                        else
                        {
                            ProvideStream(stream);
                        }
                    }
                }

                // If the stream is not connected, wait briefly to allow
                // the runtime instance to possibly repopulate a new connection stream.
                if (!isConnected)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), token);
                }
            }
            while (!isConnected);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                ProvideStream(stream: null);

                _disposed = true;
            }
        }

        protected void ProvideStream(Stream stream)
        {
            Stream previousStream;

            lock (_targets)
            {
                // Get the previous stream in order to dispose it later
                previousStream = _stream;

                // If there are any targets waiting for a stream, provide
                // it to the first target in the queue.
                if (_targets.Count > 0)
                {
                    while (null != stream)
                    {
                        StreamTarget target = _targets.Dequeue();
                        if (target.SetStream(stream))
                        {
                            stream = null;
                        }
                        else
                        {
                            // The target didn't accept the stream; this is due
                            // to the thread that registered the target no longer
                            // needing the stream (e.g. it was async awaiting and
                            // was cancelled). Dispose the target to release any
                            // resources it may have.
                            target.Dispose();
                        }
                    }
                }

                // Store the stream for when a target registers for the stream. If
                // a target was already provided the stream, this will be null, thus
                // representing that no existing stream is waiting to be consumed.
                _stream = stream;
            }

            // Dispose the previous stream if there was one.
            previousStream?.Dispose();
        }

        protected virtual Stream RefreshStream()
        {
            return null;
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

        private void RegisterTarget(StreamTarget target)
        {
            lock (_targets)
            {
                // Allow transport specific implementation to refresh
                // the stream before possibly consuming it.
                if (null == _stream)
                {
                    _stream = RefreshStream();
                }

                // If there is no current stream, add the target to the queue;
                // it will be fulfilled at some point in the future when streams
                // are made available. Otherwise, provide the stream to the target
                // synchronously; set the current stream to null to signify that
                // there is no current stream available.
                if (null == _stream)
                {
                    _targets.Enqueue(target);
                }
                else if (target.SetStream(_stream))
                {
                    _stream = null;
                }
            }
        }

        /// <summary>
        /// Base class for providing streams to callers.
        /// </summary>
        private abstract class StreamTarget : IDisposable
        {
            public void Dispose()
            {
                if (!IsDisposed)
                {
                    Dispose(disposing: true);

                    IsDisposed = true;
                }
            }

            protected abstract void Dispose(bool disposing);

            public abstract bool SetStream(Stream stream);

            public bool IsDisposed { get; private set; }
        }

        /// <summary>
        /// Class allowing for synchronously waiting on the availability of a stream.
        /// </summary>
        private class StreamEventTarget : StreamTarget
        {
            private readonly ManualResetEvent _streamEvent = new ManualResetEvent(false);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _streamEvent.Dispose();
                }
            }

            public override bool SetStream(Stream stream)
            {
                if (IsDisposed)
                {
                    return false;
                }

                _streamEvent.Set();
                Stream = stream;
                return true;
            }

            public WaitHandle Handle => _streamEvent;

            public Stream Stream { get; private set; }
        }

        /// <summary>
        /// Class allowing for asynchronously waiting on the availability of a stream.
        /// </summary>
        private class StreamTaskTarget : StreamTarget
        {
            private readonly IDisposable _registration;
            private readonly TaskCompletionSource<Stream> _streamSource = new TaskCompletionSource<Stream>();

            public StreamTaskTarget(CancellationToken token)
            {
                _registration = token.Register(() => _streamSource.TrySetCanceled());
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _registration.Dispose();
                }
            }

            public override bool SetStream(Stream stream)
            {
                if (IsDisposed)
                {
                    return false;
                }

                return _streamSource.TrySetResult(stream);
            }

            public Task<Stream> Task => _streamSource.Task;
        }
    }

    internal class ServerIpcEndpoint : BaseIpcEndpoint, IIpcEndpoint, IDisposable
    {
        /// <summary>
        /// Updates the endpoint with a new diagnostics stream.
        /// </summary>
        internal void SetStream(Stream stream)
        {
            ProvideStream(stream);
        }
    }

    internal class PidIpcEndpoint : BaseIpcEndpoint, IIpcEndpoint
    {
        private static double ConnectTimeoutMilliseconds { get; } = TimeSpan.FromSeconds(3).TotalMilliseconds;
        public static string IpcRootPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe\" : Path.GetTempPath();
        public static string DiagnosticsPortPattern { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"^dotnet-diagnostic-(\d+)$" : @"^dotnet-diagnostic-(\d+)-(\d+)-socket$";

        private int _pid;

        /// <summary>
        /// Creates a reference to a .NET process's IPC Transport
        /// using the default rules for a given pid
        /// </summary>
        /// <param name="pid">The pid of the target process</param>
        /// <returns>A reference to the IPC Transport</returns>
        public PidIpcEndpoint(int pid)
        {
            _pid = pid;
        }

        protected override Stream RefreshStream()
        {
            try
            {
                var process = Process.GetProcessById(_pid);
            }
            catch (ArgumentException)
            {
                throw new ServerNotAvailableException($"Process {_pid} is not running.");
            }
            catch (InvalidOperationException)
            {
                throw new ServerNotAvailableException($"Process {_pid} seems to be elevated.");
            }

            if (!TryGetTransportName(_pid, out string transportName))
            {
                throw new ServerNotAvailableException($"Process {_pid} not running compatible .NET runtime.");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var namedPipe = new NamedPipeClientStream(
                    ".", transportName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation);
                namedPipe.Connect((int)ConnectTimeoutMilliseconds);
                return namedPipe;
            }
            else
            {
                var socket = new UnixDomainSocket();
                socket.Connect(Path.Combine(IpcRootPath, transportName));
                return new ExposedSocketNetworkStream(socket, ownsSocket: true);
            }
        }

        private static bool TryGetTransportName(int pid, out string transportName)
        {
            transportName = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                transportName = $"dotnet-diagnostic-{pid}";
            }
            else
            {
                try
                {
                    transportName = Directory.GetFiles(IpcRootPath, $"dotnet-diagnostic-{pid}-*-socket") // Try best match.
                        .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                        .FirstOrDefault();
                }
                catch (InvalidOperationException)
                {
                }
            }

            return !string.IsNullOrEmpty(transportName);
        }
    }
}
