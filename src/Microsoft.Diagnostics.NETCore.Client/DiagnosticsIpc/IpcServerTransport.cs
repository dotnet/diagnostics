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
        public static IpcServerTransport Create(string transportPath, int maxConnections)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsPipeServerTransport(transportPath, maxConnections);
            }
            else
            {
                return new UnixDomainSocketServerTransport(transportPath, maxConnections);
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public abstract Task<Stream> AcceptAsync(CancellationToken token);

        public static int MaxAllowedConnections
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return NamedPipeServerStream.MaxAllowedServerInstances;
                }
                else
                {
                    return (int)SocketOptionName.MaxConnections;
                }
            }
        }
    }

    internal sealed class WindowsPipeServerTransport : IpcServerTransport
    {
        private const string PipePrefix = @"\\.\pipe\";

        private bool _disposed = false;
        private NamedPipeServerStream _stream;

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly string _pipeName;
        private readonly int _maxInstances;

        public WindowsPipeServerTransport(string pipeName, int maxInstances)
        {
            _maxInstances = maxInstances;
            _pipeName = pipeName.StartsWith(PipePrefix) ? pipeName.Substring(PipePrefix.Length) : pipeName;
            CreateNewPipeServer();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cancellation.Cancel();

                    _stream.Dispose();

                    _cancellation.Dispose();
                }
                _disposed = true;
            }
        }

        public override async Task<Stream> AcceptAsync(CancellationToken token)
        {
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);

            NamedPipeServerStream connectedStream;
            try
            {
                await _stream.WaitForConnectionAsync(linkedSource.Token);

                connectedStream = _stream;
            }
            finally
            {
                if (!_cancellation.IsCancellationRequested)
                {
                    CreateNewPipeServer();
                }
            }
            return connectedStream;
        }

        private void CreateNewPipeServer()
        {
            _stream = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, _maxInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }
    }

    internal sealed class UnixDomainSocketServerTransport : IpcServerTransport
    {
        private bool _disposed = false;

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly int _backlog;
        private readonly string _path;

        private UnixDomainSocketWithCleanup _socket;

        public UnixDomainSocketServerTransport(string path, int backlog)
        {
            _backlog = backlog;
            _path = path;

            CreateNewSocketServer();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
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
                _disposed = true;
            }
        }

        public override async Task<Stream> AcceptAsync(CancellationToken token)
        {
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);
            using (linkedSource.Token.Register(() => _socket.Close(0)))
            {
                Socket socket;
                try
                {
                    socket = await Task.Factory.FromAsync(_socket.BeginAccept, _socket.EndAccept, _socket);
                }
                catch (Exception ex)
                {
                    // Recreate socket if transport is not disposed.
                    if (!_cancellation.IsCancellationRequested)
                    {
                        CreateNewSocketServer();
                    }

                    // When the socket is closed, the FromAsync logic will try to call EndAccept on the socket,
                    // but that will throw an ObjectDisposedException.
                    if (ex is ObjectDisposedException)
                    {
                        // First check if the cancellation token caused the closing of the socket,
                        // then rethrow the exception if it did not.
                        token.ThrowIfCancellationRequested();
                    }

                    throw;
                }

                return new ExposedSocketNetworkStream(socket, ownsSocket: true);
            }
        }

        private void CreateNewSocketServer()
        {
            _socket = new UnixDomainSocketWithCleanup();
            _socket.Bind(_path);
            _socket.Listen(_backlog);
            _socket.LingerState.Enabled = false;
        }
    }
}
