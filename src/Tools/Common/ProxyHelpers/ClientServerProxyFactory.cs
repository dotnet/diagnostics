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
using System.Diagnostics;

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
    internal class ClientServerProxyFactory
    {
        readonly string _clientAddress;
        readonly string _serverAddress;

        ReversedDiagnosticsServer _server;
        IpcEndpointInfo _endpointInfo;

        public int RuntimeInstanceConnectTimeout { get; set; } = 30000;
        public int ClientStreamConnectTimeout { get; set; } = 5000;
        public int ServerStreamConnectTimeout { get; set; } = 5000;

        public ClientServerProxyFactory(string clientAddress, string serverAddress)
        {
            _clientAddress = clientAddress;
            _serverAddress = serverAddress;

            _server = new ReversedDiagnosticsServer(_serverAddress, true);
            _endpointInfo = new IpcEndpointInfo();
        }

        public void Start()
        {
            _server.Start();
        }

        public async void Stop()
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }

        public void ResetServerEndpoint()
        {
            if (_endpointInfo.Endpoint != null)
            {
                _server.RemoveConnection(_endpointInfo.RuntimeInstanceCookie);
                _endpointInfo = new IpcEndpointInfo();
            }
        }

        public async Task<ConnectedProxy> ConnectProxyAsync(CancellationToken token, TaskCompletionSource proxyTaskCompleted)
        {
            Stream serverStream = null;
            Stream clientStream = null;

            Debug.WriteLine($"ClientServerProxyFactory::ConnectProxyAsync: Trying to connect new proxy instance.");

            try
            {
                // Connect new server endpoint.
                serverStream = await ConnectServerStreamAsync(token);

                // Connect new client endpoint.
                clientStream = await ConnectClientStreamAsync(token);
            }
            catch (Exception)
            {
                Debug.WriteLine("ClientServerProxyFactory::ConnectProxyAsync: Failed connecting new proxy instance.");

                // Cleanup and rethrow.
                serverStream?.Dispose();
                clientStream?.Dispose();

                throw;
            }

            // Create new proxy.
            Debug.WriteLine($"ClientServerProxyFactory::ConnectProxyAsync: New proxy instance successfully connected.");
            return new ConnectedProxy(clientStream, serverStream, proxyTaskCompleted);
        }

        async Task<Stream> ConnectServerStreamAsync(CancellationToken token)
        {
            Stream serverStream;

            Debug.WriteLine($"ClientServerProxyFactory::ConnectServerStreamAsync: Connecting new TCP/IP endpoint.");

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
                        Debug.WriteLine("ClientServerProxyFactory::ConnectServerStreamAsync: No runtime instance connected, timing out.");
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
                    Debug.WriteLine("ClientServerProxyFactory::ConnectServerStreamAsync: No server stream connected, timing out.");
                    throw new ServerStreamConnectTimeoutException(ServerStreamConnectTimeout);
                }

                throw;
            }

            return serverStream;
        }

        async Task<Stream> ConnectClientStreamAsync(CancellationToken token)
        {
            Stream clientStream;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Debug.WriteLine($"ClientServerProxyFactory::ConnectClientStreamAsync: Connecting new NamedPipe endpoint \"{_clientAddress}\".");

                var namedPipe = new NamedPipeClientStream(
                    ".",
                    _clientAddress,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous,
                    TokenImpersonationLevel.Impersonation);

                try
                {
                    await namedPipe.ConnectAsync(ClientStreamConnectTimeout, token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    Debug.WriteLine("ClientServerProxyFactory::ConnectClientStreamAsync: No client stream connected, timing out.");
                    throw;
                }

                clientStream = namedPipe;
            }
            else
            {
                Debug.WriteLine($"ClientServerProxyFactory::ConnectClientStreamAsync: Connecting new UnixDomainSocket client {_clientAddress}.");

                var unixDomainSocket = new IpcUnixDomainSocketTransport(_clientAddress);

                using var connectTimeoutTokenSource = new CancellationTokenSource();
                using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

                try
                {
                    connectTimeoutTokenSource.CancelAfter(ClientStreamConnectTimeout);
                    await unixDomainSocket.ConnectAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (connectTimeoutTokenSource.IsCancellationRequested)
                    {
                        Debug.WriteLine("ClientServerProxyFactory::ConnectClientStreamAsync: No client stream connected, timing out.");
                        throw new TimeoutException();
                    }

                    throw;
                }

                clientStream = new ExposedSocketNetworkStream(unixDomainSocket, ownsSocket: true);
            }

            // ReversedDiagnosticServer consumes advertise message, needs to be replayed back to client.
            await IpcAdvertise.SerializeAsync(clientStream, _endpointInfo.RuntimeInstanceCookie, (ulong)_endpointInfo.ProcessId, token).ConfigureAwait(false);

            return clientStream;
        }

        internal class ConnectedProxy : IDisposable
        {
            Stream _clientStream = null;
            Stream _serverStream = null;

            Task _serverReadClientWriteTask = null;
            Task _clientReadServerWriteTask = null;

            CancellationTokenSource cancelProxyTokenSource = new CancellationTokenSource();

            bool _disposed = false;

            ulong _serverClientByteTransfer;
            ulong _clientServerByteTransfer;

            static int s_proxyInstanceCount;

            public TaskCompletionSource ProxyTaskCompleted { get; }

            public ConnectedProxy(Stream clientStream, Stream serverStream, TaskCompletionSource proxyTaskCompleted)
            {
                _clientStream = clientStream;
                _serverStream = serverStream;
                ProxyTaskCompleted = proxyTaskCompleted;

                _serverClientByteTransfer += (ulong)IpcAdvertise.V1SizeInBytes;

                Interlocked.Increment(ref s_proxyInstanceCount);
            }

            public void Start()
            {
                if (_serverReadClientWriteTask != null || _clientReadServerWriteTask != null || _disposed)
                    throw new InvalidOperationException();

                _serverReadClientWriteTask = ServerReadClientWrite(cancelProxyTokenSource.Token);
                _clientReadServerWriteTask = ClientReadServerWrite(cancelProxyTokenSource.Token);
            }

            public async void Stop()
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ConnectedProxy));

                cancelProxyTokenSource.Cancel();

                List<Task> runningTasks = new List<Task>();

                if (_serverReadClientWriteTask != null)
                    runningTasks.Add(_serverReadClientWriteTask);

                if (_clientReadServerWriteTask != null)
                    runningTasks.Add(_clientReadServerWriteTask);

                await Task.WhenAll(runningTasks.ToArray()).ConfigureAwait(false);

                _serverReadClientWriteTask?.Dispose();
                _clientReadServerWriteTask?.Dispose();

                ProxyTaskCompleted?.TrySetResult();

                _serverReadClientWriteTask = null;
                _clientReadServerWriteTask = null;
            }

            public bool IsRunning
            {
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

                    _serverStream?.Dispose();
                    _clientStream?.Dispose();

                    _disposed = true;

                    Interlocked.Decrement(ref s_proxyInstanceCount);

                    Debug.WriteLine($"Diposed ConnectedProxy stats: Server->Client {_serverClientByteTransfer} bytes, Client->Server {_clientServerByteTransfer} bytes.");
                    Debug.WriteLine($"Active ConnectedProxy instances: {s_proxyInstanceCount}");
                }
            }

            async Task ServerReadClientWrite(CancellationToken token)
            {
                try
                {
                    byte[] buffer = new byte[1024];
                    while (!token.IsCancellationRequested)
                    {
                        int bytesRead = await _serverStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

                        // Check for end of stream indicating that remove end hung-up.
                        if (bytesRead == 0)
                            break;

                        _serverClientByteTransfer += (ulong)bytesRead;
                        await _clientStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    // Completing task will trigger dispose of instance and cleanup.
                    // Faliure mainly consists of closed/disposed streams and cancelation requests.
                    // Just make sure task gets complete, nothing more needs to be in response to these exceptions.
                    Debug.WriteLine("ConnectedProxy::ServerReadClientWrite: Failed stream operation. Completing task.");
                }

                ProxyTaskCompleted?.TrySetResult();
            }

            async Task ClientReadServerWrite(CancellationToken token)
            {
                try
                {
                    byte[] buffer = new byte[256];
                    while (!token.IsCancellationRequested)
                    {
                        int bytesRead = await _clientStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

                        // Check for end of stream indicating that remove end hung-up.
                        if (bytesRead == 0)
                            break;

                        _clientServerByteTransfer += (ulong)bytesRead;
                        await _serverStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);

                    }
                }
                catch (Exception)
                {
                    // Completing task will trigger dispose of instance and cleanup.
                    // Faliure mainly consists of closed/disposed streams and cancelation requests.
                    // Just make sure task gets complete, nothing more needs to be in response to these exceptions.
                    Debug.WriteLine("ConnectedProxy::ClientReadServerWrite: Failed stream operation. Completing task.");
                }

                ProxyTaskCompleted?.TrySetResult();
            }
        }
    }
}
