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
                    _stream.Dispose();
                }
                _disposed = true;
            }
        }

        public override async Task<Stream> AcceptAsync(CancellationToken token)
        {
            NamedPipeServerStream connectedStream;
            try
            {
                await _stream.WaitForConnectionAsync(token);

                connectedStream = _stream;
            }
            finally
            {
                CreateNewPipeServer();
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

        private readonly string _path;
        private readonly Socket _socket;

        public UnixDomainSocketServerTransport(string path, int backlog)
        {
            _path = path;
            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _socket.Bind(PidIpcEndpoint.CreateUnixDomainSocketEndPoint(path));
            _socket.Listen(backlog);
            _socket.LingerState.Enabled = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
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

                    if (File.Exists(_path))
                        File.Delete(_path);
                }
                _disposed = true;
            }
        }

        public override async Task<Stream> AcceptAsync(CancellationToken token)
        {
            using (token.Register(() => _socket.Close(0)))
            {
                Socket socket;
                try
                {
                    socket = await Task.Factory.FromAsync(_socket.BeginAccept, _socket.EndAccept, _socket);
                }
                catch (ObjectDisposedException)
                {
                    // When the socket is close, the FromAsync logic will try to call EndAccept on the socket,
                    // but that will throw an ObjectDisposedException. First check if the cancellation token
                    // caused the closing of the socket, then rethrow the exception if it did not.
                    token.ThrowIfCancellationRequested();

                    throw;
                }

                return new ExposedSocketNetworkStream(socket, ownsSocket: true);
            }
        }
    }
}
