// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
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
        /// Gets the stream to retrieve diagnostic information from the runtime instance.
        /// </summary>
        Stream Connect();
    }

    internal class ServerIpcEndpoint : IIpcEndpoint, IDisposable
    {
        private readonly SemaphoreSlim _streamReady = new SemaphoreSlim(0);
        private readonly object _streamSync = new object();

        private bool _disposed;
        private Stream _stream;

        /// <inheritdoc cref="IIpcEndpoint.WaitForConnectionAsync"/>
        public async Task WaitForConnectionAsync(CancellationToken token)
        {
            bool isConnected = false;
            do
            {
                // Wait for the stream to be available
                await _streamReady.WaitAsync(token);

                lock (_streamSync)
                {
                    try
                    {
                        if (null == _stream)
                        {
                            throw new InvalidOperationException("Stream is null but ready semaphore was released.");
                        }
                        else if (_stream is ExposedSocketNetworkStream networkStream)
                        {
                            // Upate Connected state of socket by sending non-blocking zero-byte data.
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
                            isConnected = socket.Connected;
                        }
                        else if (_stream is PipeStream pipeStream)
                        {
                            Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Pipe stream should only be used on Windows.");

                            // PeekNamedPipe will return false if the pipe is disconnected/broken.
                            isConnected = NativeMethods.PeekNamedPipe(
                                pipeStream.SafePipeHandle,
                                null,
                                0,
                                IntPtr.Zero,
                                IntPtr.Zero,
                                IntPtr.Zero);
                        }
                    }
                    finally
                    {
                        // WaitForConnectionAsync does not consume the stream, so release the semaphore so
                        // that other operations may consume the already available stream.
                        _streamReady.Release();
                    }
                }

                // If the stream is not connected anymore, wait briefly to allow
                // the runtime instance to possibly repopulate a new connection stream.
                if (!isConnected)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), token);
                }
            }
            while (!isConnected);
        }

        /// <inheritdoc cref="IIpcEndpoint.Connect"/>
        /// <remarks>
        /// This will block until the diagnostic stream is provided. This block can happen if
        /// the stream is acquired previously and the runtime instance has not yet reconnected
        /// to the reversed diagnostics server.
        /// </remarks>
        public Stream Connect()
        {
            _streamReady.Wait();

            return ExchangeStream(stream: null);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                ExchangeStream(stream: null)?.Dispose();

                _streamReady.Dispose();

                _disposed = true;
            }
        }

        /// <summary>
        /// Updates the endpoint with a new diagnostics stream.
        /// </summary>
        internal void SetStream(Stream stream)
        {
            ExchangeStream(stream)?.Dispose();

            _streamReady.Release();
        }

        private Stream ExchangeStream(Stream stream)
        {
            lock (_streamSync)
            {
                var previousStream = _stream;
                _stream = stream;
                return previousStream;
            }
        }
    }

    internal class PidIpcEndpoint : IIpcEndpoint
    {
        private int _pid;

        private static double ConnectTimeoutMilliseconds { get; } = TimeSpan.FromSeconds(3).TotalMilliseconds;
        public static string IpcRootPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe\" : Path.GetTempPath();
        public static string DiagnosticsPortPattern { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"^dotnet-diagnostic-(\d+)$" : @"^dotnet-diagnostic-(\d+)-(\d+)-socket$";

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


        /// <inheritdoc cref="IIpcEndpoint.WaitForConnectionAsync"/>
        public async Task WaitForConnectionAsync(CancellationToken token)
        {
            bool transportExists = false;
            do
            {
                // Check if the transport path exists
                if (TryGetTransportName(_pid, out string transportName))
                {
                    transportExists = File.Exists(Path.Combine(IpcRootPath, transportName));
                }

                // If transport does not exist, wait briefly to allow
                // the runtime instance to possibly repopulate a new connection stream.
                if (!transportExists)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), token);
                }
            }
            while (!transportExists);
        }

        /// <summary>
        /// Connects to the underlying IPC Transport and opens a read/write-able Stream
        /// </summary>
        /// <returns>A Stream for writing and reading data to and from the target .NET process</returns>
        public Stream Connect()
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
