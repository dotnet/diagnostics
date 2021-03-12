// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Microsoft.Internal.Common.Utils
{
    internal class RuntimeConnectTimeoutException : TimeoutException
    {
        public RuntimeConnectTimeoutException(int timeoutMS)
            : base(string.Format("No new runtime endpoints connected, waited {0} ms", timeoutMS))
        { }
    }

    internal class ServerStreamConnectTimeoutException : TimeoutException
    {
        public ServerStreamConnectTimeoutException(int timeoutMS)
            : base(string.Format("No new server streams available, waited {0} ms", timeoutMS))
        { }
    }

    // <summary>
    // This class acts a factory class for building Client<->Server proxy instances.
    // Supports NamedPipes/UnixDomainSocket client and TCP/IP server.
    // </summary>
    internal class ClientServerICTSProxyFactory
    {
        readonly bool _verboseLogging;

        readonly string _ipcClient;
        readonly string _tcpServer;

        ReversedDiagnosticsServer _server;
        IpcEndpointInfo _endpointInfo;

        public int RuntimeInstanceConnectTimeout { get; set; } = 30000;
        public int ClientStreamConnectTimeout { get; set; } = 5000;
        public int ServerStreamConnectTimeout { get; set; } = 5000;

        public ClientServerICTSProxyFactory(string ipcClient, string tcpServer, bool verboseLogging)
        {
            _verboseLogging = verboseLogging;

            _ipcClient = ipcClient;
            _tcpServer = tcpServer;

            _server = new ReversedDiagnosticsServer(_tcpServer, true);
            _endpointInfo = new IpcEndpointInfo();
        }

        public void Start()
        {
            _server.Start();
        }

        public async Task Stop()
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }

        public void Reset()
        {
            if (_endpointInfo.Endpoint != null)
            {
                _server.RemoveConnection(_endpointInfo.RuntimeInstanceCookie);
                _endpointInfo = new IpcEndpointInfo();
            }
        }

        public async Task<ConnectedProxy> ConnectProxyAsync(CancellationToken token)
        {
            Stream serverStream = null;
            Stream clientStream = null;

            if (_verboseLogging)
                Console.WriteLine($"ClientServerICTSProxyFactory::ConnectProxyAsync: Trying to connect new proxy instance.");

            try
            {
                // Connect new server endpoint.
                serverStream = await ConnectServerStreamAsync(token);

                // Connect new client endpoint.
                clientStream = await ConnectClientStreamAsync(token);
            }
            catch (Exception)
            {
                if (_verboseLogging)
                    Console.WriteLine("ClientServerICTSProxyFactory::ConnectProxyAsync: Failed connecting new proxy instance.");

                // Cleanup and rethrow.
                serverStream?.Dispose();
                clientStream?.Dispose();

                throw;
            }

            // Create new proxy.
            if (_verboseLogging)
                Console.WriteLine($"ClientServerICTSProxyFactory::ConnectProxyAsync: New proxy instance successfully connected.");

            return new ConnectedProxy(clientStream, serverStream, _verboseLogging);
        }

        async Task<Stream> ConnectServerStreamAsync(CancellationToken token)
        {
            Stream serverStream;

            if (_verboseLogging)
                Console.WriteLine($"ClientServerICTSProxyFactory::ConnectServerStreamAsync: Connecting new TCP/IP endpoint.");

            if (_endpointInfo.Endpoint == null)
            {
                using var acceptTimeoutTokenSource = new CancellationTokenSource();
                using var acceptTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, acceptTimeoutTokenSource.Token);

                try
                {
                    // If no new runtime instance connects, timeout.
                    acceptTimeoutTokenSource.CancelAfter(RuntimeInstanceConnectTimeout);
                    _endpointInfo = await _server.AcceptAsync(acceptTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (acceptTimeoutTokenSource.IsCancellationRequested)
                    {
                        if (_verboseLogging)
                            Console.WriteLine("ClientServerICTSProxyFactory::ConnectServerStreamAsync: No runtime instance connected, timing out.");

                        throw new RuntimeConnectTimeoutException(RuntimeInstanceConnectTimeout);
                    }

                    throw;
                }
            }

            using var connectTimeoutTokenSource = new CancellationTokenSource();
            using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

            try
            {
                // Get next connected server endpoint. Should timeout if no endpoint appears within timeout.
                // If that happens we need to remove endpoint since it might indicate a unresponsive runtime instance.
                connectTimeoutTokenSource.CancelAfter(ServerStreamConnectTimeout);
                serverStream = await _endpointInfo.Endpoint.ConnectAsync(connectTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (connectTimeoutTokenSource.IsCancellationRequested)
                {
                    if (_verboseLogging)
                        Console.WriteLine("ClientServerICTSProxyFactory::ConnectServerStreamAsync: No server stream connected, timing out.");

                    throw new ServerStreamConnectTimeoutException(ServerStreamConnectTimeout);
                }

                throw;
            }

            return serverStream;
        }

        async Task<Stream> ConnectClientStreamAsync(CancellationToken token)
        {
            Stream clientStream = null;

            if (_verboseLogging)
                Console.WriteLine($"ClientServerICTSProxyFactory::ConnectClientStreamAsync: Connecting new IPC endpoint \"{_ipcClient}\".");

            // TODO: Retry connect if we detect that server stream is still alive after timeout client connection
            // This will prevent proxy from disconnecting working runtime connection while waiting for client to respond.
            // Needs to be responsive to disconnected runtime connections to react on runtime termination.

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var namedPipe = new NamedPipeClientStream(
                    ".",
                    _ipcClient,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous,
                    TokenImpersonationLevel.Impersonation);

                try
                {
                    await namedPipe.ConnectAsync(ClientStreamConnectTimeout, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    namedPipe?.Dispose();

                    if (ex is TimeoutException && _verboseLogging)
                        Console.WriteLine("ClientServerICTSProxyFactory::ConnectClientStreamAsync: No client stream connected, timing out.");

                    throw;
                }

                clientStream = namedPipe;
            }
            else
            {
                var unixDomainSocket = new IpcUnixDomainSocketTransport(_ipcClient);

                using var connectTimeoutTokenSource = new CancellationTokenSource();
                using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

                try
                {
                    connectTimeoutTokenSource.CancelAfter(ClientStreamConnectTimeout);
                    await unixDomainSocket.ConnectAsync(token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    unixDomainSocket?.Dispose();

                    if (connectTimeoutTokenSource.IsCancellationRequested)
                    {
                        if (_verboseLogging)
                            Console.WriteLine("ClientServerICTSProxyFactory::ConnectClientStreamAsync: No client stream connected, timing out.");

                        throw new TimeoutException();
                    }

                    throw;
                }
                finally
                {
                    connectTokenSource?.Dispose();
                    connectTimeoutTokenSource?.Dispose();
                }

                clientStream = new ExposedSocketNetworkStream(unixDomainSocket, ownsSocket: true);
            }

            try
            {
                // ReversedDiagnosticServer consumes advertise message, needs to be replayed back to client.
                await IpcAdvertise.SerializeAsync(clientStream, _endpointInfo.RuntimeInstanceCookie, (ulong)_endpointInfo.ProcessId, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                clientStream?.Dispose();
            }

            return clientStream;
        }
    }

    internal class ConnectedProxy : IDisposable
    {
        readonly bool _verboseLogging;

        Stream _clientStream = null;
        Stream _serverStream = null;

        Task _serverReadClientWriteTask = null;
        Task _clientReadServerWriteTask = null;

        CancellationTokenSource _cancelProxyTokenSource = null;

        bool _disposed = false;

        ulong _serverClientByteTransfer;
        ulong _clientServerByteTransfer;

        static int s_proxyInstanceCount;

        public TaskCompletionSource<bool> ProxyTaskCompleted { get; }

        public ConnectedProxy(Stream clientStream, Stream serverStream, bool verboseLogging)
        {
            _verboseLogging = verboseLogging;

            _clientStream = clientStream;
            _serverStream = serverStream;

            _cancelProxyTokenSource = new CancellationTokenSource();

            ProxyTaskCompleted = new TaskCompletionSource<bool>();

            _serverClientByteTransfer += (ulong)IpcAdvertise.V1SizeInBytes;

            Interlocked.Increment(ref s_proxyInstanceCount);
        }

        public void Start()
        {
            if (_serverReadClientWriteTask != null || _clientReadServerWriteTask != null || _disposed)
                throw new InvalidOperationException();

            _serverReadClientWriteTask = ServerReadClientWrite(_cancelProxyTokenSource.Token);
            _clientReadServerWriteTask = ClientReadServerWrite(_cancelProxyTokenSource.Token);
        }

        public async void Stop()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConnectedProxy));

            _cancelProxyTokenSource.Cancel();

            List<Task> runningTasks = new List<Task>();

            if (_serverReadClientWriteTask != null)
                runningTasks.Add(_serverReadClientWriteTask);

            if (_clientReadServerWriteTask != null)
                runningTasks.Add(_clientReadServerWriteTask);

            await Task.WhenAll(runningTasks.ToArray()).ConfigureAwait(false);

            _serverReadClientWriteTask?.Dispose();
            _clientReadServerWriteTask?.Dispose();

            ProxyTaskCompleted?.TrySetResult(true);

            _serverReadClientWriteTask = null;
            _clientReadServerWriteTask = null;
        }

        public bool IsRunning {
            get
            {
                if (_serverReadClientWriteTask == null || _clientReadServerWriteTask == null || _disposed)
                    return false;

                return !_serverReadClientWriteTask.IsCompleted && !_clientReadServerWriteTask.IsCompleted;
            }
        }
        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();

                _cancelProxyTokenSource.Dispose();

                _serverStream?.Dispose();
                _clientStream?.Dispose();

                _disposed = true;

                Interlocked.Decrement(ref s_proxyInstanceCount);

                if (_verboseLogging)
                {
                    Console.WriteLine($"ConnectedProxy: Diposed stats: Server->Client {_serverClientByteTransfer} bytes, Client->Server {_clientServerByteTransfer} bytes.");
                    Console.WriteLine($"ConnectedProxy: Active instances: {s_proxyInstanceCount}");
                }
            }
        }

        async Task ServerReadClientWrite(CancellationToken token)
        {
            try
            {
                byte[] buffer = new byte[1024];
                while (!token.IsCancellationRequested)
                {
#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine("ConnectedProxy::ServerReadClientWrite: Start reading bytes from server.");
#endif

                    int bytesRead = await _serverStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::ServerReadClientWrite: Read {bytesRead} bytes from server.");
#endif

                    // Check for end of stream indicating that remove end hung-up.
                    if (bytesRead == 0)
                    {
                        if (_verboseLogging)
                            Console.WriteLine("ConnectedProxy::ServerReadClientWrite: Server hung up.");

                        break;
                    }

                    _serverClientByteTransfer += (ulong)bytesRead;

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::ServerReadClientWrite: Start writing {bytesRead} bytes to client.");
#endif

                    await _clientStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                    await _clientStream.FlushAsync().ConfigureAwait(false);

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::ServerReadClientWrite: Wrote {bytesRead} bytes to client.");
#endif
                }
            }
            catch (Exception)
            {
                // Completing task will trigger dispose of instance and cleanup.
                // Faliure mainly consists of closed/disposed streams and cancelation requests.
                // Just make sure task gets complete, nothing more needs to be in response to these exceptions.
                if (_verboseLogging)
                    Console.WriteLine("ConnectedProxy::ServerReadClientWrite: Failed stream operation. Completing task.");
            }

            ProxyTaskCompleted?.TrySetResult(true);
        }

        async Task ClientReadServerWrite(CancellationToken token)
        {
            try
            {
                byte[] buffer = new byte[1024];
                while (!token.IsCancellationRequested)
                {
#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine("ConnectedProxy::ClientReadServerWrite: Start reading bytes from client.");
#endif

                    int bytesRead = await _clientStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::ClientReadServerWrite: Read {bytesRead} bytes from client.");
#endif

                    // Check for end of stream indicating that remote end hung-up.
                    if (bytesRead == 0)
                    {
                        if (_verboseLogging)
                            Console.WriteLine("ConnectedProxy::ClientReadServerWrite: Client hung up.");

                        break;
                    }

                    _clientServerByteTransfer += (ulong)bytesRead;

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::ClientReadServerWrite: Start writing {bytesRead} bytes to server.");
#endif

                    await _serverStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                    await _serverStream.FlushAsync().ConfigureAwait(false);

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::ClientReadServerWrite: Wrote {bytesRead} bytes to server.");
#endif
                }
            }
            catch (Exception)
            {
                // Completing task will trigger dispose of instance and cleanup.
                // Faliure mainly consists of closed/disposed streams and cancelation requests.
                // Just make sure task gets complete, nothing more needs to be in response to these exceptions.
                if (_verboseLogging)
                    Console.WriteLine("ConnectedProxy::ClientReadServerWrite: Failed stream operation. Completing task.");
            }

            ProxyTaskCompleted?.TrySetResult(true);
        }
    }
}
