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

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// An endpoint used to acquire the diagnostics stream for a runtime instance.
    /// </summary>
    internal interface IIpcEndpoint
    {
        /// <summary>
        /// Checks that the client is able to communicate with target process over diagnostic transport.
        /// </summary>
        /// <returns>
        /// True if client is able to communicate with target process; otherwise, false.
        /// </returns>
        bool CheckTransport();

        /// <summary>
        /// Gets the stream to retrieve diagnostic information from the runtime instance.
        /// </summary>
        Stream Connect();
    }

    internal class ServerIpcEndpoint : IIpcEndpoint, IDisposable
    {
        private readonly AutoResetEvent _streamReady = new AutoResetEvent(false);
        private readonly object _streamSync = new object();

        private bool _disposed;
        private Stream _stream;

        /// <inheritdoc cref="IIpcEndpoint.CheckTransport"/>
        public bool CheckTransport()
        {
            if (!_streamReady.WaitOne(0))
            {
                return false;
            }

            // Stream was available; reset event should be in wait state at this time
            // so anything that tries to get the stream via Connect will block momentarily.
            // This means this method should have exclusive access to mutating or checking
            // the state of the stream.

            lock (_streamSync)
            {
                try
                {
                    if (null == _stream)
                    {
                        throw new InvalidOperationException("Stream is null but ready event was set.");
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
                        return socket.Connected;
                    }
                    else if (_stream is PipeStream pipeStream)
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            // PeekNamedPipe will return false if the pipe is disconnected/broken.
                            if (!NativeMethods.PeekNamedPipe(pipeStream.SafePipeHandle, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                            {
                                int err = Marshal.GetLastWin32Error();
                                if (109 == err) // ERROR_BROKEN_PIPE
                                {
                                    return false;
                                }
                                throw new InvalidOperationException($"PeekNamedPipe failed: {err}");
                            }
                            return true;
                        }
                    }

                    throw new InvalidOperationException($"Stream type '{_stream.GetType().FullName}' was not handled.");
                }
                finally
                {
                    // Set the event again to allow other callers to get the stream.
                    _streamReady.Set();
                }
            }
        }

        /// <inheritdoc cref="IIpcEndpoint.Connect"/>
        /// <remarks>
        /// This will block until the diagnostic stream is provided. This block can happen if
        /// the stream is acquired previously and the runtime instance has not yet reconnected
        /// to the reversed diagnostics server.
        /// </remarks>
        public Stream Connect()
        {
            _streamReady.WaitOne();

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

            _streamReady.Set();
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


        /// <inheritdoc cref="IIpcEndpoint.CheckTransport"/>
        public bool CheckTransport()
        {
            if (!TryGetTransportName(_pid, out string transportName))
            {
                return false;
            }

            return File.Exists(Path.Combine(IpcRootPath, transportName));
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
                string path = Path.Combine(IpcRootPath, transportName);
                var remoteEP = CreateUnixDomainSocketEndPoint(path);

                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(remoteEP);
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

        internal static EndPoint CreateUnixDomainSocketEndPoint(string path)
        {
#if NETCOREAPP
            return new UnixDomainSocketEndPoint(path);
#elif NETSTANDARD2_0
            // UnixDomainSocketEndPoint is not part of .NET Standard 2.0
            var type = typeof(Socket).Assembly.GetType("System.Net.Sockets.UnixDomainSocketEndPoint");
            if (type == null)
            {
                throw new PlatformNotSupportedException("Current process is not running a compatible .NET Core runtime.");
            }
            var ctor = type.GetConstructor(new[] { typeof(string) });
            return (EndPoint)ctor.Invoke(new object[] { path });
#endif
        }
    }
}
