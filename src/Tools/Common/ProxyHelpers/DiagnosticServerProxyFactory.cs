// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Internal.Common.Utils
{
    internal class RuntimeConnectTimeoutException : TimeoutException
    {
        public RuntimeConnectTimeoutException(int timeoutMS)
            : base(string.Format("No new runtime endpoints connected, waited {0} ms", timeoutMS))
        { }
    }

    internal class BackendStreamConnectTimeoutException : TimeoutException
    {
        public BackendStreamConnectTimeoutException(int timeoutMS)
            : base(string.Format("No new backend streams available, waited {0} ms", timeoutMS))
        { }
    }

    // <summary>
    // Class used to run different flavours of Diagnostic Server proxies.
    // </summary>
    internal class DiagnosticServerProxyFactory
    {
        public static async Task<int> runIpcClientTcpServerProxy(CancellationToken token, string ipcClient, string tcpServer, bool autoShutdown, bool debug)
        {
            Console.WriteLine($"DiagnosticServerProxy: Starting IPC client <--> TCP server proxy using IPC client endpoint=\"{ipcClient}\" and TCP server endpoint=\"{tcpServer}\".");
            return await runProxy(token, new IpcClientTcpServerProxy(ipcClient, tcpServer, debug), autoShutdown, debug).ConfigureAwait(false);
        }

        async static Task<int> runProxy(CancellationToken token, DiagnosticServerProxy proxy, bool autoShutdown, bool debug)
        {
            List<Task> runningTasks = new List<Task>();
            List<ConnectedProxy> runningProxies = new List<ConnectedProxy>();

            try
            {
                proxy.Start();
                while (!token.IsCancellationRequested)
                {
                    Task<ConnectedProxy> connectedProxyTask = null;
                    ConnectedProxy connectedProxy = null;

                    try
                    {
                        connectedProxyTask = proxy.ConnectProxyAsync(token);

                        do
                        {
                            // Search list and clean up dead proxy instances before continue waiting on new instances.
                            runningProxies.RemoveAll(IsConnectedProxyDead);

                            runningTasks.Clear();
                            foreach (var runningProxy in runningProxies)
                                runningTasks.Add(runningProxy.ProxyTaskCompleted.Task);
                            runningTasks.Add(connectedProxyTask);
                        }
                        while (await Task.WhenAny(runningTasks.ToArray()).ConfigureAwait(false) != connectedProxyTask);

                        if (connectedProxyTask.IsFaulted || connectedProxyTask.IsCanceled)
                        {
                            //Throw original exception.
                            connectedProxyTask.GetAwaiter().GetResult();
                        }

                        if (connectedProxyTask.IsCompleted)
                        {
                            connectedProxy = connectedProxyTask.Result;
                            connectedProxy.Start();

                            // Add to list of running proxy instances.
                            runningProxies.Add(connectedProxy);
                            connectedProxy = null;
                        }

                        connectedProxyTask.Dispose();
                        connectedProxyTask = null;
                    }
                    catch (Exception ex)
                    {
                        connectedProxy?.Dispose();
                        connectedProxy = null;

                        connectedProxyTask?.Dispose();
                        connectedProxyTask = null;

                        // Timing out on accepting new streams could mean that either the frontend holds an open connection
                        // alive (but currently not using it), or we have a dead backend. If there are no running
                        // proxies we assume a dead backend. Reset current backend endpoint and see if we get
                        // reconnect using same or different runtime instance.
                        if (ex is BackendStreamConnectTimeoutException && runningProxies.Count == 0)
                        {
                            if (autoShutdown || debug)
                                Console.WriteLine("DiagnosticServerProxyFactory: No backend stream available before timeout.");

                            proxy.Reset();
                        }

                        // Timing out on accepting a new runtime connection means there is no runtime alive.
                        // Shutdown proxy to prevent instances to outlive runtime process (if auto shutdown is enabled).
                        if (ex is RuntimeConnectTimeoutException)
                        {
                            if (autoShutdown || debug)
                                Console.WriteLine("DiagnosticServerProxyFactory: No runtime connected before timeout.");

                            if (autoShutdown)
                            {
                                Console.WriteLine("DiagnosticServerProxyFactory: Starting automatic proxy server shutdown.");
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DiagnosticServerProxyFactory: Shutting down due to error: {ex.Message}");
            }
            finally
            {
                runningProxies.RemoveAll(IsConnectedProxyDead);
                runningProxies.Clear();

                await proxy?.Stop();

                Console.WriteLine("DiagnosticServerProxyFactory: Stopped.");
            }
            return 0;
        }

        static bool IsConnectedProxyDead(ConnectedProxy connectedProxy)
        {
            bool isRunning = connectedProxy.IsRunning && !connectedProxy.ProxyTaskCompleted.Task.IsCompleted;
            if (!isRunning)
                connectedProxy.Dispose();
            return !isRunning;
        }
    }

    // <summary>
    // Base class representing a Diagnostic Server proxy.
    // </summary>
    internal class DiagnosticServerProxy
    {
        public virtual void Start()
        {
            throw new NotImplementedException();
        }

        public virtual Task Stop()
        {
            throw new NotImplementedException();
        }

        public virtual void Reset()
        {
            throw new NotImplementedException();
        }

        public virtual Task<ConnectedProxy> ConnectProxyAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }

    // <summary>
    // This class represent a TCP/IP server endpoint used when building up proxy instances.
    // </summary>
    internal class TcpServerProxy : DiagnosticServerProxy
    {
        protected readonly bool _verboseLogging;

        readonly string _tcpServer;

        ReversedDiagnosticsServer _server;
        IpcEndpointInfo _endpointInfo;

        public int RuntimeInstanceConnectTimeout { get; set; } = 60000;
        public int TcpServerConnectTimeout { get; set; } = 5000;

        public Guid RuntimeInstanceId
        {
            get { return _endpointInfo.RuntimeInstanceCookie; }
        }

        public int RuntimeProcessId
        {
            get { return _endpointInfo.ProcessId; }
        }

        protected TcpServerProxy(string tcpServer, bool verboseLogging)
        {
            _verboseLogging = verboseLogging;

            _tcpServer = tcpServer;

            _server = new ReversedDiagnosticsServer(_tcpServer, true);
            _endpointInfo = new IpcEndpointInfo();
        }

        public override void Start()
        {
            _server.Start();
        }

        public override async Task Stop()
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }

        public override void Reset()
        {
            if (_endpointInfo.Endpoint != null)
            {
                _server.RemoveConnection(_endpointInfo.RuntimeInstanceCookie);
                _endpointInfo = new IpcEndpointInfo();
            }
        }

        protected async Task<Stream> ConnectTcpStreamAsync(CancellationToken token)
        {
            Stream tcpServerStream;

            if (_verboseLogging)
                Console.WriteLine($"TcpServerProxy::ConnectTcpStreamAsync: Connecting new tcp endpoint.");

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
                            Console.WriteLine("TcpServerProxy::ConnectTcpStreamAsync: No runtime instance connected, timing out.");

                        throw new RuntimeConnectTimeoutException(RuntimeInstanceConnectTimeout);
                    }

                    throw;
                }
            }

            using var connectTimeoutTokenSource = new CancellationTokenSource();
            using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

            try
            {
                // Get next connected tcp server stream. Should timeout if no endpoint appears within timeout.
                // If that happens we need to remove endpoint since it might indicate a unresponsive runtime instance.
                connectTimeoutTokenSource.CancelAfter(TcpServerConnectTimeout);
                tcpServerStream = await _endpointInfo.Endpoint.ConnectAsync(connectTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (connectTimeoutTokenSource.IsCancellationRequested)
                {
                    if (_verboseLogging)
                        Console.WriteLine("TcpServerProxy::ConnectTcpStreamAsync: No tcp stream connected, timing out.");

                    throw new BackendStreamConnectTimeoutException(TcpServerConnectTimeout);
                }

                throw;
            }

            if (tcpServerStream != null && _verboseLogging)
                Console.WriteLine($"TcpServerProxy::ConnectTcpStreamAsync: Successfully connected tcp stream, runtime id={RuntimeInstanceId}, runtime pid={RuntimeProcessId}.");

            return tcpServerStream;
        }

        protected bool CheckTcpStreamConnection(Stream tcpStream, CancellationToken token)
        {
            Debug.Assert(tcpStream is ExposedSocketNetworkStream, "Tcp stream should be an ExposedSocketNetworkStream.");

            bool connected = true;
            var networkStream = tcpStream as ExposedSocketNetworkStream;

            bool blockingState = networkStream.Socket.Blocking;
            try
            {
                // Check connection by peek one byte. Will return 0 in case connection is closed.
                // A closed connection could also raise exception, but then socket connected state should
                // be set to false.
                networkStream.Socket.Blocking = false;
                if (networkStream.Socket.Receive(new byte[1], 0, 1, System.Net.Sockets.SocketFlags.Peek) == 0)
                    connected = false;
            }
            catch (Exception)
            {
                connected = networkStream.Socket.Connected;
            }
            finally
            {
                networkStream.Socket.Blocking = blockingState;
            }

            return connected;
        }
    }

    // <summary>
    // This class connects IPC Client<-> TCP Server proxy instances.
    // Supports NamedPipes/UnixDomainSocket client and TCP/IP server.
    // </summary>
    internal class IpcClientTcpServerProxy : TcpServerProxy
    {
        readonly string _ipcClient;

        public int IpcClientConnectTimeout { get; set; } = 5000;

        public int IpcClientConnectRetryAttempts { get; set; } = 0;

        public IpcClientTcpServerProxy(string ipcClient, string tcpServer, bool verboseLogging)
            : base(tcpServer, verboseLogging)
        {
            _ipcClient = ipcClient;
        }

        public override async Task<ConnectedProxy> ConnectProxyAsync(CancellationToken token)
        {
            Stream tcpServerStream = null;
            Stream ipcClientStream = null;

            if (_verboseLogging)
                Console.WriteLine($"IpcClientTcpServerProxy::ConnectProxyAsync: Trying to connect new proxy instance.");

            try
            {
                // Connect new server endpoint.
                tcpServerStream = await ConnectTcpStreamAsync(token).ConfigureAwait(false);

                int retryAttempts = 0;
                while (!token.IsCancellationRequested && ipcClientStream == null)
                {
                    try
                    {
                        // Connect new client endpoint.
                        ipcClientStream = await ConnectIpcStreamAsync(token).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        retryAttempts++;

                        if (IpcClientConnectRetryAttempts != -1 && retryAttempts > IpcClientConnectRetryAttempts)
                        {
                            throw;
                        }

                        // Check if tcp server stream connection is still available. If so, we can retry client connection
                        // keeping existing accepted tcp server connection.
                        if (!CheckTcpStreamConnection(tcpServerStream, token))
                            throw;

                        if (_verboseLogging)
                            Console.WriteLine($"IpcClientTcpServerProxy::ConnectProxyAsync: Retrying client connection {retryAttempts} of {IpcClientConnectRetryAttempts} attempts.");

                        ipcClientStream?.Dispose();
                        ipcClientStream = null;
                    }
                }
            }
            catch (Exception)
            {
                if (_verboseLogging)
                    Console.WriteLine("IpcClientTcpServerProxy::ConnectProxyAsync: Failed connecting new proxy instance.");

                // Cleanup and rethrow.
                tcpServerStream?.Dispose();
                ipcClientStream?.Dispose();

                throw;
            }

            // Create new proxy.
            if (_verboseLogging)
                Console.WriteLine($"IpcClientTcpServerProxy::ConnectProxyAsync: New proxy instance successfully connected.");

            return new ConnectedProxy(ipcClientStream, tcpServerStream, _verboseLogging);
        }

        protected async Task<Stream> ConnectIpcStreamAsync(CancellationToken token)
        {
            Stream ipcClientStream = null;

            if (_verboseLogging)
                Console.WriteLine($"IpcClientTcpServerProxy::ConnectIpcStreamAsync: Connecting new ipc endpoint \"{_ipcClient}\".");

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
                    await namedPipe.ConnectAsync(IpcClientConnectTimeout, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    namedPipe?.Dispose();

                    if (ex is TimeoutException && _verboseLogging)
                        Console.WriteLine("IpcClientTcpServerProxy::ConnectIpcStreamAsync: No ipc stream connected, timing out.");

                    throw;
                }

                ipcClientStream = namedPipe;
            }
            else
            {
                var unixDomainSocket = new IpcUnixDomainSocketTransport(_ipcClient);

                using var connectTimeoutTokenSource = new CancellationTokenSource();
                using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

                try
                {
                    connectTimeoutTokenSource.CancelAfter(IpcClientConnectTimeout);
                    await unixDomainSocket.ConnectAsync(token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    unixDomainSocket?.Dispose();

                    if (connectTimeoutTokenSource.IsCancellationRequested)
                    {
                        if (_verboseLogging)
                            Console.WriteLine("IpcClientTcpServerProxy::ConnectIpcStreamAsync: No ipc stream connected, timing out.");

                        throw new TimeoutException();
                    }

                    throw;
                }

                ipcClientStream = new ExposedSocketNetworkStream(unixDomainSocket, ownsSocket: true);
            }

            try
            {
                // ReversedDiagnosticServer consumes advertise message, needs to be replayed back to client. Use proxy process ID as representation.
                await IpcAdvertise.SerializeAsync(ipcClientStream, RuntimeInstanceId, (ulong)Process.GetCurrentProcess().Id, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (_verboseLogging)
                    Console.WriteLine("IpcClientTcpServerProxy::ConnectIpcStreamAsync: Failed sending advertise message.");

                ipcClientStream?.Dispose();
                throw;
            }

            if (ipcClientStream != null && _verboseLogging)
                Console.WriteLine($"IpcClientTcpServerProxy::ConnectIpcStreamAsync: Successfully connected ipc stream.");

            return ipcClientStream;
        }
    }

    internal class ConnectedProxy : IDisposable
    {
        readonly bool _verboseLogging;

        Stream _frontendStream = null;
        Stream _backendStream = null;

        Task _backendReadFrontendWriteTask = null;
        Task _frontendReadBackendWriteTask = null;

        CancellationTokenSource _cancelProxyTokenSource = null;

        bool _disposed = false;

        ulong _backendToFrontendByteTransfer;
        ulong _frontendToBackendByteTransfer;

        static int s_proxyInstanceCount;

        public TaskCompletionSource<bool> ProxyTaskCompleted { get; }

        public ConnectedProxy(Stream frontendStream, Stream backendStream, bool verboseLogging)
        {
            _verboseLogging = verboseLogging;

            _frontendStream = frontendStream;
            _backendStream = backendStream;

            _cancelProxyTokenSource = new CancellationTokenSource();

            ProxyTaskCompleted = new TaskCompletionSource<bool>();

            _backendToFrontendByteTransfer += (ulong)IpcAdvertise.V1SizeInBytes;

            Interlocked.Increment(ref s_proxyInstanceCount);
        }

        public void Start()
        {
            if (_backendReadFrontendWriteTask != null || _frontendReadBackendWriteTask != null || _disposed)
                throw new InvalidOperationException();

            _backendReadFrontendWriteTask = BackendReadFrontendWrite(_cancelProxyTokenSource.Token);
            _frontendReadBackendWriteTask = FrontendReadBackendWrite(_cancelProxyTokenSource.Token);
        }

        public async void Stop()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConnectedProxy));

            _cancelProxyTokenSource.Cancel();

            List<Task> runningTasks = new List<Task>();

            if (_backendReadFrontendWriteTask != null)
                runningTasks.Add(_backendReadFrontendWriteTask);

            if (_frontendReadBackendWriteTask != null)
                runningTasks.Add(_frontendReadBackendWriteTask);

            await Task.WhenAll(runningTasks.ToArray()).ConfigureAwait(false);

            _backendReadFrontendWriteTask?.Dispose();
            _frontendReadBackendWriteTask?.Dispose();

            ProxyTaskCompleted?.TrySetResult(true);

            _backendReadFrontendWriteTask = null;
            _frontendReadBackendWriteTask = null;
        }

        public bool IsRunning {
            get
            {
                if (_backendReadFrontendWriteTask == null || _frontendReadBackendWriteTask == null || _disposed)
                    return false;

                return !_backendReadFrontendWriteTask.IsCompleted && !_frontendReadBackendWriteTask.IsCompleted;
            }
        }
        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();

                _cancelProxyTokenSource.Dispose();

                _backendStream?.Dispose();
                _frontendStream?.Dispose();

                _disposed = true;

                Interlocked.Decrement(ref s_proxyInstanceCount);

                if (_verboseLogging)
                {
                    Console.WriteLine($"ConnectedProxy: Diposed stats: Backend->Frontend {_backendToFrontendByteTransfer} bytes, Frontend->Backend {_frontendToBackendByteTransfer} bytes.");
                    Console.WriteLine($"ConnectedProxy: Active instances: {s_proxyInstanceCount}");
                }
            }
        }

        async Task BackendReadFrontendWrite(CancellationToken token)
        {
            try
            {
                byte[] buffer = new byte[1024];
                while (!token.IsCancellationRequested)
                {
#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine("ConnectedProxy::BackendReadFrontendWrite: Start reading bytes from backend.");
#endif

                    int bytesRead = await _backendStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::BackendReadFrontendWrite: Read {bytesRead} bytes from backend.");
#endif

                    // Check for end of stream indicating that remote end hung-up.
                    if (bytesRead == 0)
                    {
                        if (_verboseLogging)
                            Console.WriteLine("ConnectedProxy::BackendReadFrontendWrite: Backend hung up.");

                        break;
                    }

                    _backendToFrontendByteTransfer += (ulong)bytesRead;

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::BackendReadFrontendWrite: Start writing {bytesRead} bytes to frontend.");
#endif

                    await _frontendStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                    await _frontendStream.FlushAsync().ConfigureAwait(false);

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::BackendReadFrontendWrite: Wrote {bytesRead} bytes to frontend.");
#endif
                }
            }
            catch (Exception)
            {
                // Completing task will trigger dispose of instance and cleanup.
                // Faliure mainly consists of closed/disposed streams and cancelation requests.
                // Just make sure task gets complete, nothing more needs to be in response to these exceptions.
                if (_verboseLogging)
                    Console.WriteLine("ConnectedProxy::BackendReadFrontendWrite: Failed stream operation. Completing task.");
            }

            ProxyTaskCompleted?.TrySetResult(true);
        }

        async Task FrontendReadBackendWrite(CancellationToken token)
        {
            try
            {
                byte[] buffer = new byte[1024];
                while (!token.IsCancellationRequested)
                {
#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine("ConnectedProxy::FrontendReadBackendWrite: Start reading bytes from frotend.");
#endif

                    int bytesRead = await _frontendStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::FrontendReadBackendWrite: Read {bytesRead} bytes from frontend.");
#endif

                    // Check for end of stream indicating that remote end hung-up.
                    if (bytesRead == 0)
                    {
                        if (_verboseLogging)
                            Console.WriteLine("ConnectedProxy::FrontendReadBackendWrite: Frontend hung up.");

                        break;
                    }

                    _frontendToBackendByteTransfer += (ulong)bytesRead;

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::FrontendReadBackendWrite: Start writing {bytesRead} bytes to backend.");
#endif

                    await _backendStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                    await _backendStream.FlushAsync().ConfigureAwait(false);

#if DEBUG
                    if (_verboseLogging)
                        Console.WriteLine($"ConnectedProxy::FrontendReadBackendWrite: Wrote {bytesRead} bytes to backend.");
#endif
                }
            }
            catch (Exception)
            {
                // Completing task will trigger dispose of instance and cleanup.
                // Faliure mainly consists of closed/disposed streams and cancelation requests.
                // Just make sure task gets complete, nothing more needs to be in response to these exceptions.
                if (_verboseLogging)
                    Console.WriteLine("ConnectedProxy::FrontendReadBackendWrite: Failed stream operation. Completing task.");
            }

            ProxyTaskCompleted?.TrySetResult(true);
        }
    }
}
