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
        public static IpcServerTransport Create(string transportPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsPipeServerTransport(transportPath);
            }
            else
            {
                return new UnixDomainSocketServerTransport(transportPath);
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
    }

    internal sealed class WindowsPipeServerTransport : IpcServerTransport
    {
        private const string PipePrefix = @"\\.\pipe\";

        private bool _disposed = false;
        private NamedPipeServerStream _stream;

        private readonly string _pipeName;

        public WindowsPipeServerTransport(string pipeName)
        {
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
            _stream = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 10, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }
    }

    internal sealed class UnixDomainSocketServerTransport : IpcServerTransport
    {
        private bool _disposed = false;

        private readonly string _path;
        private readonly Socket _socket;

        public UnixDomainSocketServerTransport(string path)
        {
            _path = path;
            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _socket.Bind(PidIpcEndpoint.CreateUnixDomainSocketEndPoint(path));
            _socket.Listen(255);
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
            TaskCompletionSource<Socket> acceptCompletionSource = new TaskCompletionSource<Socket>(TaskCreationOptions.RunContinuationsAsynchronously);

            Action cancelAccept = () =>
            {
                acceptCompletionSource.TrySetCanceled(token);
                _socket.Close(0);
            };

            AsyncCallback endAccept = result =>
            {
                if (!token.IsCancellationRequested)
                {
                    try
                    {
                        acceptCompletionSource.TrySetResult(_socket.EndAccept(result));
                    }
                    catch (Exception ex)
                    {
                        acceptCompletionSource.TrySetException(ex);
                    }
                }
            };

            using (token.Register(cancelAccept))
            {
                _socket.BeginAccept(endAccept, null);

                return new NetworkStream(await acceptCompletionSource.Task);
            }
        }
    }
}
