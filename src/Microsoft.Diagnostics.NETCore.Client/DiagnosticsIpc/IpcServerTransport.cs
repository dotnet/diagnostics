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
        private bool _disposed;

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

        protected void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }
        }
    }

    internal sealed class WindowsPipeServerTransport : IpcServerTransport
    {
        private const string PipePrefix = @"\\.\pipe\";

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

            NamedPipeServerStream connectedStream;
            try
            {
                await _stream.WaitForConnectionAsync(linkedSource.Token).ConfigureAwait(false);

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
            _stream = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                _maxInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }
    }

    internal sealed class UnixDomainSocketServerTransport : IpcServerTransport
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly int _backlog;
        private readonly string _path;

        private UnixDomainSocket _socket;

        public UnixDomainSocketServerTransport(string path, int backlog)
        {
            _backlog = backlog;
            _path = path;

            CreateNewSocketServer();
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
                Socket socket = await _socket.AcceptAsync(linkedSource.Token).ConfigureAwait(false);

                return new ExposedSocketNetworkStream(socket, ownsSocket: true);
            }
            catch (Exception)
            {
                // Recreate socket if transport is not disposed.
                if (!_cancellation.IsCancellationRequested)
                {
                    CreateNewSocketServer();
                }
                throw;
            }
        }

        private void CreateNewSocketServer()
        {
            _socket = new UnixDomainSocket();
            _socket.Bind(_path);
            _socket.Listen(_backlog);
            _socket.LingerState.Enabled = false;
        }
    }
}
