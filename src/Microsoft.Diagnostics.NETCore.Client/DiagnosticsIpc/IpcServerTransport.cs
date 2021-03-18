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
            if (!enableTcpIpProtocol || !IpcTcpSocketTransport.TryParseIPAddress(address))
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
        private readonly int _backlog;
        private readonly string _address;

        private IpcSocketTransport _socket;

        public IpcSocketServerTransport(string address, int backlog)
        {
            _backlog = backlog;
            _address = address;

            _socket = CreateNewSocketServer(_address, _backlog);
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
                    _socket = CreateNewSocketServer(_address, _backlog);
                }
                throw;
            }
        }

        internal abstract bool OnAccept(Socket socket);

        internal abstract IpcSocketTransport CreateNewSocketServer(string address, int backlog);
    }

    internal sealed class IpcTcpSocketServerTransport : IpcSocketServerTransport
{
        public IpcTcpSocketServerTransport(string address, int backlog)
            : base (address, backlog != MaxAllowedConnections ? backlog : 100)
        {
        }

        internal override bool OnAccept(Socket socket)
        {
            socket.NoDelay = true;
            return true;
        }

        internal override IpcSocketTransport CreateNewSocketServer(string address, int backlog)
        {
            string hostAddress;
            int hostPort;

            if (!IpcTcpSocketTransport.TryParseIPAddress(address, out hostAddress, out hostPort))
                throw new ArgumentException(string.Format("Could not parse {0} into host, port", address));

            var socket = IpcTcpSocketTransport.Create(hostAddress, hostPort);
            socket.Bind();
            socket.Listen(backlog);
            socket.LingerState.Enabled = false;
            OnCreateNewServer();
            return socket;
        }
    }

    internal sealed class IpcUnixDomainSocketServerTransport : IpcSocketServerTransport
    {
        public IpcUnixDomainSocketServerTransport(string path, int backlog)
            : base (path, backlog != MaxAllowedConnections ? backlog : (int)SocketOptionName.MaxConnections)
        {
        }

        internal override bool OnAccept(Socket socket)
        {
            return true;
        }

        internal override IpcSocketTransport CreateNewSocketServer(string address, int backlog)
        {
            var socket = IpcUnixDomainSocketTransport.Create(address);
            socket.Bind();
            socket.Listen(backlog);
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
