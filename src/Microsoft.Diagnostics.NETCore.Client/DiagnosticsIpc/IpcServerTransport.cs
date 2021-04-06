// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal abstract class IpcServerTransport : IDisposable
    {
        private IIpcServerTransportCallbackInternal _callback;
        private bool _disposed;

        public static IpcServerTransport Create(string address, int maxConnections, bool enableTcpIpProtocol)
        {
            if (!enableTcpIpProtocol || !IpcTcpSocket.TryParseIPAddress(address, out _, out _))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return new IpcWindowsNamedPipeServerTransport(address, maxConnections);
                }
                else
                {
                    return new IpcUnixDomainSocketServerTransport(address, maxConnections);
                }
            }
            else
            {
                return new IpcTcpSocketServerTransport(address, maxConnections);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Dispose(disposing: true);

                _disposed = true;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public abstract Task<Stream> AcceptAsync(CancellationToken token);

        public static int MaxAllowedConnections {
            get
            {
                return -1;
            }
        }

        protected void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }
        }

        internal void SetCallback(IIpcServerTransportCallbackInternal callback)
        {
            _callback = callback;
        }

        protected void OnCreateNewServer()
        {
            _callback?.CreatedNewServer();
        }
    }

    internal sealed class IpcWindowsNamedPipeServerTransport : IpcServerTransport
    {
        private const string PipePrefix = @"\\.\pipe\";

        private NamedPipeServerStream _stream;

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly string _pipeName;
        private readonly int _maxInstances;

        public IpcWindowsNamedPipeServerTransport(string pipeName, int maxInstances)
        {
            _maxInstances = maxInstances != MaxAllowedConnections ? maxInstances : NamedPipeServerStream.MaxAllowedServerInstances;
            _pipeName = pipeName.StartsWith(PipePrefix) ? pipeName.Substring(PipePrefix.Length) : pipeName;
            _stream = CreateNewNamedPipeServer(_pipeName, _maxInstances);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cancellation.Cancel();

                _stream.Dispose();

                _cancellation.Dispose();
            }
        }

        public override async Task<Stream> AcceptAsync(CancellationToken token)
        {
            VerifyNotDisposed();

            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);
            try
            {
                // Connect client to named pipe server stream.
                await _stream.WaitForConnectionAsync(linkedSource.Token).ConfigureAwait(false);

                // Transfer ownership of connected named pipe.
                var connectedStream = _stream;

                // Setup new named pipe server stream used in upcomming accept calls.
                _stream = CreateNewNamedPipeServer(_pipeName, _maxInstances);

                return connectedStream;
            }
            catch (Exception)
            {
                // Keep named pipe server stream when getting any kind of cancel request.
                // Cancel happens when complete transport is about to disposed or caller
                // cancels out specific accept call, no need to recycle named pipe server stream.
                // In all other exception scenarios named pipe server stream will be re-created.
                if (!linkedSource.IsCancellationRequested)
                {
                    _stream.Dispose();
                    _stream = CreateNewNamedPipeServer(_pipeName, _maxInstances);
                }
                throw;
            }
        }

        private NamedPipeServerStream CreateNewNamedPipeServer(string pipeName, int maxInstances)
        {
            var stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, maxInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            OnCreateNewServer();
            return stream;
        }
    }

    internal abstract class IpcSocketServerTransport : IpcServerTransport
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private IpcSocket _socket;

        public IpcSocketServerTransport()
        {
            _socket = CreateNewSocketServer();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cancellation.Cancel();

                try
                {
                    _socket.Shutdown(SocketShutdown.Both);
                }
                catch { }
                finally
                {
                    _socket.Close(0);
                }
                _socket.Dispose();

                _cancellation.Dispose();
            }
        }

        public override async Task<Stream> AcceptAsync(CancellationToken token)
        {
            VerifyNotDisposed();

            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);
            try
            {
                // Accept next client socket.
                var socket = await _socket.AcceptAsync(linkedSource.Token).ConfigureAwait(false);

                // Configure client socket based on transport type.
                OnAccept(socket);

                return new ExposedSocketNetworkStream(socket, ownsSocket: true);
            }
            catch (Exception)
            {
                // Keep server socket when getting any kind of cancel request.
                // Cancel happens when complete transport is about to disposed or caller
                // cancels out specific accept call, no need to recycle server socket.
                // In all other exception scenarios server socket will be re-created.
                if (!linkedSource.IsCancellationRequested)
                {
                    _socket = CreateNewSocketServer();
                }
                throw;
            }
        }

        internal abstract bool OnAccept(Socket socket);

        internal abstract IpcSocket CreateNewSocketServer();
    }

    internal sealed class IpcTcpSocketServerTransport : IpcSocketServerTransport
    {
        private readonly int _backlog;
        private readonly string _hostAddress;
        private readonly int _hostPort;

        public IpcTcpSocketServerTransport(string address, int backlog)
        {
            _backlog = backlog != MaxAllowedConnections ? backlog : 100;
            if (!IpcTcpSocket.TryParseIPAddress(address, out _hostAddress, out _hostPort))
                throw new ArgumentException(string.Format("Could not parse {0} into host, port", address));
        }

        internal override bool OnAccept(Socket socket)
        {
            socket.NoDelay = true;
            return true;
        }

        internal override IpcSocket CreateNewSocketServer()
        {
            var socket = IpcTcpSocket.Create(_hostAddress, _hostPort);
            socket.Bind();
            socket.Listen(_backlog);
            socket.LingerState.Enabled = false;
            OnCreateNewServer();
            return socket;
        }
    }

    internal sealed class IpcUnixDomainSocketServerTransport : IpcSocketServerTransport
    {
        private readonly int _backlog;
        private readonly string _path;

        public IpcUnixDomainSocketServerTransport(string path, int backlog)
        {
            _backlog = backlog != MaxAllowedConnections ? backlog : (int)SocketOptionName.MaxConnections;
            _path = path;
        }

        internal override bool OnAccept(Socket socket)
        {
            return true;
        }

        internal override IpcSocket CreateNewSocketServer()
        {
            var socket = IpcUnixDomainSocket.Create(_path);
            socket.Bind();
            socket.Listen(_backlog);
            socket.LingerState.Enabled = false;
            OnCreateNewServer();
            return socket;
        }
    }

    internal interface IIpcServerTransportCallbackInternal
    {
        void CreatedNewServer();
    }
}
