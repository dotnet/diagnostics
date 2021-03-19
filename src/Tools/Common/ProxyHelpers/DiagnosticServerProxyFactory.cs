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
using System.Net;
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
            Console.WriteLine($"Starting IPC client ({ipcClient}) <--> TCP server ({tcpServer}) router.");
            return await runProxy(token, new IpcClientTcpServerProxy(ipcClient, tcpServer, debug), autoShutdown, debug).ConfigureAwait(false);
        }

        public static async Task<int> runIpcServerTcpServerProxy(CancellationToken token, string ipcServer, string tcpServer, bool autoShutdown, bool debug)
        {
            if (string.IsNullOrEmpty(ipcServer))
                ipcServer = IpcServerTcpServerProxy.GetDefaultIpcServerPath();

            Console.WriteLine($"Starting IPC server ({ipcServer}) <--> TCP server ({tcpServer}) router.");
            return await runProxy(token, new IpcServerTcpServerProxy(ipcServer, tcpServer, debug), autoShutdown, debug).ConfigureAwait(false);
        }

        public static bool isLoopbackOnly(string address)
        {
            bool isLooback = false;

            try
            {
                var value = IpcTcpSocketTransport.ResolveIPAddress(address);
                isLooback = IPAddress.IsLoopback(value.Address);
            }
            catch { }

            return isLooback;
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
                                Console.WriteLine("No backend stream available before timeout.");

                            proxy.Reset();
                        }

                        // Timing out on accepting a new runtime connection means there is no runtime alive.
                        // Shutdown proxy to prevent instances to outlive runtime process (if auto shutdown is enabled).
                        if (ex is RuntimeConnectTimeoutException)
                        {
                            if (autoShutdown || debug)
                                Console.WriteLine("No runtime connected before timeout.");

                            if (autoShutdown)
                            {
                                Console.WriteLine("Starting automatic proxy server shutdown.");
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Shutting proxy server down due to error: {ex.Message}");
            }
            finally
            {
                if (token.IsCancellationRequested)
                    Console.WriteLine("Shutting down proxy server due to cancelation request.");

                runningProxies.RemoveAll(IsConnectedProxyDead);
                runningProxies.Clear();

                await proxy?.Stop();

                Console.WriteLine("Proxy server stopped.");
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

        readonly string _tcpServerAddress;

        ReversedDiagnosticsServer _tcpServer;
        IpcEndpointInfo _tcpServerEndpointInfo;

        public int RuntimeInstanceConnectTimeout { get; set; } = 60000;
        public int TcpServerConnectTimeout { get; set; } = 5000;

        public Guid RuntimeInstanceId
        {
            get { return _tcpServerEndpointInfo.RuntimeInstanceCookie; }
        }

        public int RuntimeProcessId
        {
            get { return _tcpServerEndpointInfo.ProcessId; }
        }

        protected TcpServerProxy(string tcpServer, bool verboseLogging)
        {
            _verboseLogging = verboseLogging;

            _tcpServerAddress = tcpServer;

            _tcpServer = new ReversedDiagnosticsServer(_tcpServerAddress, true);
            _tcpServerEndpointInfo = new IpcEndpointInfo();
        }

        public override void Start()
        {
            _tcpServer.Start();
        }

        public override async Task Stop()
        {
            await _tcpServer.DisposeAsync().ConfigureAwait(false);
        }

        public override void Reset()
        {
            if (_tcpServerEndpointInfo.Endpoint != null)
            {
                _tcpServer.RemoveConnection(_tcpServerEndpointInfo.RuntimeInstanceCookie);
                _tcpServerEndpointInfo = new IpcEndpointInfo();
            }
        }

        protected async Task<Stream> ConnectTcpStreamAsync(CancellationToken token)
        {
            Stream tcpServerStream;

            if (_verboseLogging)
                Console.WriteLine($"TcpServerProxy::ConnectTcpStreamAsync: Waiting for new tcp connection at endpoint \"{_tcpServerAddress}\".");

            if (_tcpServerEndpointInfo.Endpoint == null)
            {
                using var acceptTimeoutTokenSource = new CancellationTokenSource();
                using var acceptTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, acceptTimeoutTokenSource.Token);

                try
                {
                    // If no new runtime instance connects, timeout.
                    acceptTimeoutTokenSource.CancelAfter(RuntimeInstanceConnectTimeout);
                    _tcpServerEndpointInfo = await _tcpServer.AcceptAsync(acceptTokenSource.Token).ConfigureAwait(false);
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
                tcpServerStream = await _tcpServerEndpointInfo.Endpoint.ConnectAsync(connectTokenSource.Token).ConfigureAwait(false);
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

        protected bool CheckStreamConnection(Stream stream, CancellationToken token)
        {
            bool connected = true;

            if (stream is NamedPipeServerStream || stream is NamedPipeClientStream)
            {
                PipeStream pipeStream = stream as PipeStream;

                // PeekNamedPipe will return false if the pipe is disconnected/broken.
                connected = NativeMethods.PeekNamedPipe(
                    pipeStream.SafePipeHandle,
                    null,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            else if (stream is ExposedSocketNetworkStream networkStream)
            {
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
            }
            else
            {
                connected = false;
            }

            return connected;
        }

        protected async Task CheckStreamConnectionAsync(Stream stream, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Check if tcp stream connection is still available.
                if (!CheckStreamConnection(stream, token))
                {
                    throw new EndOfStreamException();
                }

                try
                {
                    await Task.Delay(TcpServerConnectTimeout, token).ConfigureAwait(false);
                }
                catch { }
            }
        }
    }

    // <summary>
    // This class connects IPC Server<-> TCP Server proxy instances.
    // Supports NamedPipes/UnixDomainSocket server and TCP/IP server.
    // </summary>
    internal class IpcServerTcpServerProxy : TcpServerProxy
    {
        readonly string _ipcServerPath;

        IpcServerTransport _ipcServer;

        public int IpcServerConnectTimeout { get; set; } = Timeout.Infinite;

        public IpcServerTcpServerProxy(string ipcServer, string tcpServer, bool verboseLogging)
            : base(tcpServer, verboseLogging)
        {
            _ipcServerPath = ipcServer;
            if (string.IsNullOrEmpty(_ipcServerPath))
                _ipcServerPath = GetDefaultIpcServerPath();

            _ipcServer = IpcServerTransport.Create(_ipcServerPath, IpcServerTransport.MaxAllowedConnections, false);
        }

        public static string GetDefaultIpcServerPath()
        {
            int processId = Process.GetCurrentProcess().Id;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-{processId}");
            }
            else
            {
                TimeSpan diff = Process.GetCurrentProcess().StartTime.ToUniversalTime() - DateTime.UnixEpoch;
                return Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-{processId}-{(long)diff.TotalSeconds}-socket");
            }
        }

        public override Task Stop()
        {
            _ipcServer?.Dispose();
            return base.Stop();
        }

        public override async Task<ConnectedProxy> ConnectProxyAsync(CancellationToken token)
        {
            Stream tcpServerStream = null;
            Stream ipcServerStream = null;

            if (_verboseLogging)
                Console.WriteLine($"IpcServerTcpServerProxy::ConnectProxyAsync: Trying to connect new proxy instance.");

            try
            {
                using CancellationTokenSource cancelConnectProxy = CancellationTokenSource.CreateLinkedTokenSource(token);

                // Connect new tcp server endpoint.
                using var tcpServerStreamTask = ConnectTcpStreamAsync(cancelConnectProxy.Token);

                // Connect new ipc server endpoint.
                using var ipcServerStreamTask = ConnectIpcStreamAsync(cancelConnectProxy.Token);

                await Task.WhenAny(ipcServerStreamTask, tcpServerStreamTask).ConfigureAwait(false);

                if (ipcServerStreamTask.IsCompletedSuccessfully && tcpServerStreamTask.IsCompletedSuccessfully)
                {
                    ipcServerStream = ipcServerStreamTask.Result;
                    tcpServerStream = tcpServerStreamTask.Result;
                }
                else if (ipcServerStreamTask.IsCompletedSuccessfully)
                {
                    ipcServerStream = ipcServerStreamTask.Result;
                    tcpServerStream = await tcpServerStreamTask.ConfigureAwait(false);
                }
                else if (tcpServerStreamTask.IsCompletedSuccessfully)
                {
                    tcpServerStream = tcpServerStreamTask.Result;

                    // We have a valid tcp server endpoint and a pending connect ipc stream. Wait for completion
                    // or disconnect of tcp server endpoint.
                    using var checkTcpStreamTask = CheckStreamConnectionAsync(tcpServerStream, cancelConnectProxy.Token);

                    // Wait for at least completion of one task.
                    await Task.WhenAny(ipcServerStreamTask, checkTcpStreamTask).ConfigureAwait(false);

                    // Cancel out any pending tasks not yet completed.
                    cancelConnectProxy.Cancel();

                    try
                    {
                        await Task.WhenAll(ipcServerStreamTask, checkTcpStreamTask).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Check if we have an accepted ipc server stream.
                        if (ipcServerStreamTask.IsCompletedSuccessfully)
                            ipcServerStreamTask.Result?.Dispose();

                        if (checkTcpStreamTask.IsFaulted)
                        {
                            Console.WriteLine($"IpcServerTcpServerProxy::ConnectProxyAsync: Broken tcp server connection detected, aborting ipc connection.");
                            checkTcpStreamTask.GetAwaiter().GetResult();
                        }

                        throw;
                    }

                    ipcServerStream = ipcServerStreamTask.Result;
                }
                else
                {
                    // Error case, cancel out. wait and throw exception.
                    cancelConnectProxy.Cancel();
                    try
                    {
                        await Task.WhenAll(ipcServerStreamTask, tcpServerStreamTask).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Check if we have an accepted ipc server stream.
                        if (ipcServerStreamTask.IsCompletedSuccessfully)
                            ipcServerStreamTask.Result?.Dispose();
                        throw;
                    }
                }
            }
            catch (Exception)
            {
                if (_verboseLogging)
                    Console.WriteLine("IpcServerTcpServerProxy::ConnectProxyAsync: Failed connecting new proxy instance.");

                // Cleanup and rethrow.
                ipcServerStream?.Dispose();
                tcpServerStream?.Dispose();

                throw;
            }

            // Create new proxy.
            if (_verboseLogging)
                Console.WriteLine($"IpcServerTcpServerProxy::ConnectProxyAsync: New proxy instance successfully connected.");

            return new ConnectedProxy(ipcServerStream, tcpServerStream, _verboseLogging);
        }

        protected async Task<Stream> ConnectIpcStreamAsync(CancellationToken token)
        {
            Stream ipcServerStream = null;

            if (_verboseLogging)
                Console.WriteLine($"IpcServerTcpServerProxy::ConnectIpcStreamAsync: Waiting for new ipc connection at endpoint \"{_ipcServerPath}\".");


            using var connectTimeoutTokenSource = new CancellationTokenSource();
            using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

            try
            {
                connectTimeoutTokenSource.CancelAfter(IpcServerConnectTimeout);
                ipcServerStream = await _ipcServer.AcceptAsync(connectTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                ipcServerStream?.Dispose();

                if (connectTimeoutTokenSource.IsCancellationRequested)
                {
                    if (_verboseLogging)
                        Console.WriteLine("IpcServerTcpServerProxy::ConnectIpcStreamAsync: No ipc stream connected, timing out.");

                    throw new TimeoutException();
                }

                throw;
            }

            if (ipcServerStream != null && _verboseLogging)
                Console.WriteLine($"IpcServerTcpServerProxy::ConnectIpcStreamAsync: Successfully connected ipc stream.");

            return ipcServerStream;
        }
    }

    // <summary>
    // This class connects IPC Client<-> TCP Server proxy instances.
    // Supports NamedPipes/UnixDomainSocket client and TCP/IP server.
    // </summary>
    internal class IpcClientTcpServerProxy : TcpServerProxy
    {
        readonly string _ipcClientPath;

        public int IpcClientConnectTimeout { get; set; } = Timeout.Infinite;

        public int IpcClientConnectFailureTimeout { get; set; } = 500;

        public IpcClientTcpServerProxy(string ipcClient, string tcpServer, bool verboseLogging)
            : base(tcpServer, verboseLogging)
        {
            _ipcClientPath = ipcClient;
        }

        public override async Task<ConnectedProxy> ConnectProxyAsync(CancellationToken token)
        {
            Stream tcpServerStream = null;
            Stream ipcClientStream = null;

            if (_verboseLogging)
                Console.WriteLine($"IpcClientTcpServerProxy::ConnectProxyAsync: Trying to connect new proxy instance.");

            try
            {
                using CancellationTokenSource cancelConnectProxy = CancellationTokenSource.CreateLinkedTokenSource(token);

                // Connect new server endpoint.
                tcpServerStream = await ConnectTcpStreamAsync(cancelConnectProxy.Token).ConfigureAwait(false);

                // Connect new client endpoint.
                using var ipcClientStreamTask = ConnectIpcStreamAsync(cancelConnectProxy.Token);

                // We have a valid tcp server endpoint and a pending connect ipc stream. Wait for completion
                // or disconnect of tcp server endpoint.
                using var checkTcpStreamTask = CheckStreamConnectionAsync(tcpServerStream, cancelConnectProxy.Token);

                // Wait for at least completion of one task.
                await Task.WhenAny(ipcClientStreamTask, checkTcpStreamTask).ConfigureAwait(false);

                // Cancel out any pending tasks not yet completed.
                cancelConnectProxy.Cancel();

                try
                {
                    await Task.WhenAll(ipcClientStreamTask, checkTcpStreamTask).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Check if we have an accepted ipc client stream.
                    if (ipcClientStreamTask.IsCompletedSuccessfully)
                        ipcClientStreamTask.Result?.Dispose();

                    if (checkTcpStreamTask.IsFaulted)
                    {
                        Console.WriteLine($"IpcClientTcpServerProxy::ConnectProxyAsync: Broken tcp server connection detected, aborting ipc connection.");
                        checkTcpStreamTask.GetAwaiter().GetResult();
                    }

                    throw;
                }

                ipcClientStream = ipcClientStreamTask.Result;
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

            return new ConnectedProxy(ipcClientStream, tcpServerStream, _verboseLogging, (ulong)IpcAdvertise.V1SizeInBytes);
        }

        protected async Task<Stream> ConnectIpcStreamAsync(CancellationToken token)
        {
            Stream ipcClientStream = null;

            if (_verboseLogging)
                Console.WriteLine($"IpcClientTcpServerProxy::ConnectIpcStreamAsync: Connecting new ipc endpoint \"{_ipcClientPath}\".");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var namedPipe = new NamedPipeClientStream(
                    ".",
                    _ipcClientPath,
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
                bool retry = false;
                IpcUnixDomainSocketTransport unixDomainSocket;
                do
                {
                    unixDomainSocket = new IpcUnixDomainSocketTransport(_ipcClientPath);

                    using var connectTimeoutTokenSource = new CancellationTokenSource();
                    using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

                    try
                    {
                        connectTimeoutTokenSource.CancelAfter(IpcClientConnectTimeout);
                        await unixDomainSocket.ConnectAsync(token).ConfigureAwait(false);
                        retry = false;
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

                        if (_verboseLogging)
                            Console.WriteLine($"IpcClientTcpServerProxy::ConnectIpcStreamAsync: Failed connecting {_ipcClientPath}, wait {IpcClientConnectFailureTimeout} ms before retrying.");

                        // If we get an error (without hitting timeout above), most likely due to unavailable listener.
                        // Delay execution to prevent to rapid retry attempts.
                        await Task.Delay(IpcClientConnectFailureTimeout, token).ConfigureAwait(false);

                        if (IpcClientConnectTimeout != Timeout.Infinite)
                            throw;

                        retry = true;
                    }
                }
                while (retry);

                ipcClientStream = new ExposedSocketNetworkStream(unixDomainSocket, ownsSocket: true);
            }

            try
            {
                // ReversedDiagnosticServer consumes advertise message, needs to be replayed back to ipc client stream. Use proxy process ID as representation.
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

        public ConnectedProxy(Stream frontendStream, Stream backendStream, bool verboseLogging, ulong initBackendToFrontendByteTransfer = 0, ulong initFrontendToBackendByteTransfer = 0)
        {
            _verboseLogging = verboseLogging;

            _frontendStream = frontendStream;
            _backendStream = backendStream;

            _cancelProxyTokenSource = new CancellationTokenSource();

            ProxyTaskCompleted = new TaskCompletionSource<bool>();

            _backendToFrontendByteTransfer = initBackendToFrontendByteTransfer;
            _frontendToBackendByteTransfer = initFrontendToBackendByteTransfer;

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
