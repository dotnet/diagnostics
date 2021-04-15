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
using Microsoft.Extensions.Logging;
using System.Net;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal class RuntimeTimeoutException : TimeoutException
    {
        public RuntimeTimeoutException(int TimeoutMs)
            : base(string.Format("No new runtime endpoints connected, waited {0} ms", TimeoutMs))
        { }
    }

    internal class BackendStreamTimeoutException : TimeoutException
    {
        public BackendStreamTimeoutException(int TimeoutMs)
            : base(string.Format("No new backend streams available, waited {0} ms", TimeoutMs))
        { }
    }

    /// <summary>
    /// Base class representing a Diagnostics Server router factory.
    /// </summary>
    internal class DiagnosticsServerRouterFactory
    {
        protected readonly ILogger _logger;

        public DiagnosticsServerRouterFactory(ILogger logger)
        {
            _logger = logger;
        }

        public ILogger Logger
        {
            get { return _logger; }
        }

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

        public virtual Task<Router> CreateRouterAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// This class represent a TCP/IP server endpoint used when building up router instances.
    /// </summary>
    internal class TcpServerRouterFactory : DiagnosticsServerRouterFactory
    {
        protected string _tcpServerAddress;

        protected ReversedDiagnosticsServer _tcpServer;
        protected IpcEndpointInfo _tcpServerEndpointInfo;

        protected bool _auto_shutdown;

        protected int RuntimeTimeoutMs { get; set; } = 60000;
        protected int TcpServerTimeoutMs { get; set; } = 5000;
        protected int IsStreamConnectedTimeoutMs { get; set; } = 500;

        public Guid RuntimeInstanceId
        {
            get { return _tcpServerEndpointInfo.RuntimeInstanceCookie; }
        }

        public int RuntimeProcessId
        {
            get { return _tcpServerEndpointInfo.ProcessId; }
        }

        public string TcpServerAddress
        {
            get { return _tcpServerAddress; }
        }

        protected TcpServerRouterFactory(string tcpServer, int runtimeTimeoutMs, ILogger logger)
            : base(logger)
        {
            _tcpServerAddress = IpcTcpSocketEndPoint.NormalizeTcpIpEndPoint(string.IsNullOrEmpty(tcpServer) ? "127.0.0.1:0" : tcpServer);

            _auto_shutdown = runtimeTimeoutMs != Timeout.Infinite;
            if (runtimeTimeoutMs != Timeout.Infinite)
                RuntimeTimeoutMs = runtimeTimeoutMs;

            _tcpServer = new ReversedDiagnosticsServer(_tcpServerAddress, enableTcpIpProtocol : true);
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

        protected async Task<Stream> AcceptTcpStreamAsync(CancellationToken token)
        {
            Stream tcpServerStream;

            Logger.LogDebug($"Waiting for a new tcp connection at endpoint \"{_tcpServerAddress}\".");

            if (_tcpServerEndpointInfo.Endpoint == null)
            {
                using var acceptTimeoutTokenSource = new CancellationTokenSource();
                using var acceptTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, acceptTimeoutTokenSource.Token);

                try
                {
                    // If no new runtime instance connects, timeout.
                    acceptTimeoutTokenSource.CancelAfter(RuntimeTimeoutMs);
                    _tcpServerEndpointInfo = await _tcpServer.AcceptAsync(acceptTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (acceptTimeoutTokenSource.IsCancellationRequested)
                    {
                        Logger.LogDebug("No runtime instance connected before timeout.");

                        if (_auto_shutdown)
                            throw new RuntimeTimeoutException(RuntimeTimeoutMs);
                    }

                    throw;
                }
            }

            using var connectTimeoutTokenSource = new CancellationTokenSource();
            using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

            try
            {
                // Get next connected tcp stream. Should timeout if no endpoint appears within timeout.
                // If that happens we need to remove endpoint since it might indicate a unresponsive runtime.
                connectTimeoutTokenSource.CancelAfter(TcpServerTimeoutMs);
                tcpServerStream = await _tcpServerEndpointInfo.Endpoint.ConnectAsync(connectTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (connectTimeoutTokenSource.IsCancellationRequested)
                {
                    Logger.LogDebug("No tcp stream connected before timeout.");

                    throw new BackendStreamTimeoutException(TcpServerTimeoutMs);
                }

                throw;
            }

            if (tcpServerStream != null)
                Logger.LogDebug($"Successfully connected tcp stream, runtime id={RuntimeInstanceId}, runtime pid={RuntimeProcessId}.");

            return tcpServerStream;
        }

        protected bool IsStreamConnected(Stream stream, CancellationToken token)
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
                    // Check connection read state by peek one byte. Will return 0 in case connection is closed.
                    // A closed connection could also raise exception, but then socket connected state should
                    // be set to false.
                    networkStream.Socket.Blocking = false;
                    if (networkStream.Socket.Receive(new byte[1], 0, 1, System.Net.Sockets.SocketFlags.Peek) == 0)
                        connected = false;

                    // Check connection write state by sending non-blocking zero-byte data.
                    // A closed connection should raise exception, but then socket connected state should
                    // be set to false.
                    if (connected)
                        networkStream.Socket.Send(Array.Empty<byte>(), 0, System.Net.Sockets.SocketFlags.None);
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

        protected async Task IsStreamConnectedAsync(Stream stream, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Check if tcp stream connection is still available.
                if (!IsStreamConnected(stream, token))
                {
                    throw new EndOfStreamException();
                }

                try
                {
                    // Wait before rechecking connection.
                    await Task.Delay(IsStreamConnectedTimeoutMs, token).ConfigureAwait(false);
                }
                catch { }
            }
        }

        protected bool IsCompletedSuccessfully(Task t)
        {
#if NETCOREAPP2_0_OR_GREATER
            return t.IsCompletedSuccessfully;
#else
            return t.IsCompleted && !t.IsCanceled && !t.IsFaulted;
#endif
        }
    }

    /// <summary>
    /// This class creates IPC Server - TCP Server router instances.
    /// Supports NamedPipes/UnixDomainSocket server and TCP/IP server.
    /// </summary>
    internal class IpcServerTcpServerRouterFactory : TcpServerRouterFactory, IIpcServerTransportCallbackInternal
    {
        readonly string _ipcServerPath;

        IpcServerTransport _ipcServer;

        protected int IpcServerTimeoutMs { get; set; } = Timeout.Infinite;

        public IpcServerTcpServerRouterFactory(string ipcServer, string tcpServer, int runtimeTimeoutMs, ILogger logger)
            : base(tcpServer, runtimeTimeoutMs, logger)
        {
            _ipcServerPath = ipcServer;
            if (string.IsNullOrEmpty(_ipcServerPath))
                _ipcServerPath = GetDefaultIpcServerPath();

            _ipcServer = IpcServerTransport.Create(_ipcServerPath, IpcServerTransport.MaxAllowedConnections, false);
            _tcpServer.TransportCallback = this;
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
                DateTime unixEpoch;
#if NETCOREAPP2_1_OR_GREATER
                unixEpoch = DateTime.UnixEpoch;
#else
                unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
#endif
                TimeSpan diff = Process.GetCurrentProcess().StartTime.ToUniversalTime() - unixEpoch;
                return Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-{processId}-{(long)diff.TotalSeconds}-socket");
            }
        }

        public override void Start()
        {
            base.Start();
            _logger.LogInformation($"Starting IPC server ({_ipcServerPath}) <--> TCP server ({_tcpServerAddress}) router.");
        }

        public override Task Stop()
        {
            _ipcServer?.Dispose();
            return base.Stop();
        }

        public override async Task<Router> CreateRouterAsync(CancellationToken token)
        {
            Stream tcpServerStream = null;
            Stream ipcServerStream = null;

            Logger.LogDebug($"Trying to create new router instance.");

            try
            {
                using CancellationTokenSource cancelRouter = CancellationTokenSource.CreateLinkedTokenSource(token);

                // Get new tcp server endpoint.
                using var tcpServerStreamTask = AcceptTcpStreamAsync(cancelRouter.Token);

                // Get new ipc server endpoint.
                using var ipcServerStreamTask = AcceptIpcStreamAsync(cancelRouter.Token);

                await Task.WhenAny(ipcServerStreamTask, tcpServerStreamTask).ConfigureAwait(false);

                if (IsCompletedSuccessfully(ipcServerStreamTask) && IsCompletedSuccessfully(tcpServerStreamTask))
                {
                    ipcServerStream = ipcServerStreamTask.Result;
                    tcpServerStream = tcpServerStreamTask.Result;
                }
                else if (IsCompletedSuccessfully(ipcServerStreamTask))
                {
                    ipcServerStream = ipcServerStreamTask.Result;

                    // We have a valid ipc stream and a pending tcp accept. Wait for completion
                    // or disconnect of ipc stream.
                    using var checkIpcStreamTask = IsStreamConnectedAsync(ipcServerStream, cancelRouter.Token);

                    // Wait for at least completion of one task.
                    await Task.WhenAny(tcpServerStreamTask, checkIpcStreamTask).ConfigureAwait(false);

                    // Cancel out any pending tasks not yet completed.
                    cancelRouter.Cancel();

                    try
                    {
                        await Task.WhenAll(tcpServerStreamTask, checkIpcStreamTask).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Check if we have an accepted tcp stream.
                        if (IsCompletedSuccessfully(tcpServerStreamTask))
                            tcpServerStreamTask.Result?.Dispose();

                        if (checkIpcStreamTask.IsFaulted)
                        {
                            Logger.LogInformation("Broken ipc connection detected, aborting tcp connection.");
                            checkIpcStreamTask.GetAwaiter().GetResult();
                        }

                        throw;
                    }

                    tcpServerStream = tcpServerStreamTask.Result;
                }
                else if (IsCompletedSuccessfully(tcpServerStreamTask))
                {
                    tcpServerStream = tcpServerStreamTask.Result;

                    // We have a valid tcp stream and a pending ipc accept. Wait for completion
                    // or disconnect of tcp stream.
                    using var checkTcpStreamTask = IsStreamConnectedAsync(tcpServerStream, cancelRouter.Token);

                    // Wait for at least completion of one task.
                    await Task.WhenAny(ipcServerStreamTask, checkTcpStreamTask).ConfigureAwait(false);

                    // Cancel out any pending tasks not yet completed.
                    cancelRouter.Cancel();

                    try
                    {
                        await Task.WhenAll(ipcServerStreamTask, checkTcpStreamTask).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Check if we have an accepted ipc stream.
                        if (IsCompletedSuccessfully(ipcServerStreamTask))
                            ipcServerStreamTask.Result?.Dispose();

                        if (checkTcpStreamTask.IsFaulted)
                        {
                            Logger.LogInformation("Broken tcp connection detected, aborting ipc connection.");
                            checkTcpStreamTask.GetAwaiter().GetResult();
                        }

                        throw;
                    }

                    ipcServerStream = ipcServerStreamTask.Result;
                }
                else
                {
                    // Error case, cancel out. wait and throw exception.
                    cancelRouter.Cancel();
                    try
                    {
                        await Task.WhenAll(ipcServerStreamTask, tcpServerStreamTask).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Check if we have an ipc stream.
                        if (IsCompletedSuccessfully(ipcServerStreamTask))
                            ipcServerStreamTask.Result?.Dispose();
                        throw;
                    }
                }
            }
            catch (Exception)
            {
                Logger.LogDebug("Failed creating new router instance.");

                // Cleanup and rethrow.
                ipcServerStream?.Dispose();
                tcpServerStream?.Dispose();

                throw;
            }

            // Create new router.
            Logger.LogDebug("New router instance successfully created.");

            return new Router(ipcServerStream, tcpServerStream, Logger);
        }

        protected async Task<Stream> AcceptIpcStreamAsync(CancellationToken token)
        {
            Stream ipcServerStream = null;

            Logger.LogDebug($"Waiting for new ipc connection at endpoint \"{_ipcServerPath}\".");


            using var connectTimeoutTokenSource = new CancellationTokenSource();
            using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

            try
            {
                connectTimeoutTokenSource.CancelAfter(IpcServerTimeoutMs);
                ipcServerStream = await _ipcServer.AcceptAsync(connectTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                ipcServerStream?.Dispose();

                if (connectTimeoutTokenSource.IsCancellationRequested)
                {
                    Logger.LogDebug("No ipc stream connected, timing out.");

                    throw new TimeoutException();
                }

                throw;
            }

            if (ipcServerStream != null)
                Logger.LogDebug("Successfully connected ipc stream.");

            return ipcServerStream;
        }

        public void CreatedNewServer(EndPoint localEP)
        {
            if (localEP is IPEndPoint ipEP)
                _tcpServerAddress = _tcpServerAddress.Replace(":0", string.Format(":{0}", ipEP.Port));
        }
    }

    /// <summary>
    /// This class creates IPC Client - TCP Server router instances.
    /// Supports NamedPipes/UnixDomainSocket client and TCP/IP server.
    /// </summary>
    internal class IpcClientTcpServerRouterFactory : TcpServerRouterFactory, IIpcServerTransportCallbackInternal
    {
        readonly string _ipcClientPath;

        protected int IpcClientTimeoutMs { get; set; } = Timeout.Infinite;

        protected int IpcClientRetryTimeoutMs { get; set; } = 500;

        public IpcClientTcpServerRouterFactory(string ipcClient, string tcpServer, int runtimeTimeoutMs, ILogger logger)
            : base(tcpServer, runtimeTimeoutMs, logger)
        {
            _ipcClientPath = ipcClient;
            _tcpServer.TransportCallback = this;
        }

        public override void Start()
        {
            base.Start();
            _logger.LogInformation($"Starting IPC client ({_ipcClientPath}) <--> TCP server ({_tcpServerAddress}) router.");
        }

        public override async Task<Router> CreateRouterAsync(CancellationToken token)
        {
            Stream tcpServerStream = null;
            Stream ipcClientStream = null;

            Logger.LogDebug("Trying to create a new router instance.");

            try
            {
                using CancellationTokenSource cancelRouter = CancellationTokenSource.CreateLinkedTokenSource(token);

                // Get new server endpoint.
                tcpServerStream = await AcceptTcpStreamAsync(cancelRouter.Token).ConfigureAwait(false);

                // Get new client endpoint.
                using var ipcClientStreamTask = ConnectIpcStreamAsync(cancelRouter.Token);

                // We have a valid tcp stream and a pending ipc stream. Wait for completion
                // or disconnect of tcp stream.
                using var checkTcpStreamTask = IsStreamConnectedAsync(tcpServerStream, cancelRouter.Token);

                // Wait for at least completion of one task.
                await Task.WhenAny(ipcClientStreamTask, checkTcpStreamTask).ConfigureAwait(false);

                // Cancel out any pending tasks not yet completed.
                cancelRouter.Cancel();

                try
                {
                    await Task.WhenAll(ipcClientStreamTask, checkTcpStreamTask).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Check if we have an accepted ipc stream.
                    if (IsCompletedSuccessfully(ipcClientStreamTask))
                        ipcClientStreamTask.Result?.Dispose();

                    if (checkTcpStreamTask.IsFaulted)
                    {
                        Logger.LogInformation("Broken tcp connection detected, aborting ipc connection.");
                        checkTcpStreamTask.GetAwaiter().GetResult();
                    }

                    throw;
                }

                ipcClientStream = ipcClientStreamTask.Result;
            }
            catch (Exception)
            {
                Logger.LogDebug("Failed creating new router instance.");

                // Cleanup and rethrow.
                tcpServerStream?.Dispose();
                ipcClientStream?.Dispose();

                throw;
            }

            // Create new router.
            Logger.LogDebug("New router instance successfully created.");

            return new Router(ipcClientStream, tcpServerStream, Logger, (ulong)IpcAdvertise.V1SizeInBytes);
        }

        protected async Task<Stream> ConnectIpcStreamAsync(CancellationToken token)
        {
            Stream ipcClientStream = null;

            Logger.LogDebug($"Connecting new ipc endpoint \"{_ipcClientPath}\".");

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
                    await namedPipe.ConnectAsync(IpcClientTimeoutMs, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    namedPipe?.Dispose();

                    if (ex is TimeoutException)
                        Logger.LogDebug("No ipc stream connected, timing out.");

                    throw;
                }

                ipcClientStream = namedPipe;
            }
            else
            {
                bool retry = false;
                IpcUnixDomainSocket unixDomainSocket;
                do
                {
                    unixDomainSocket = new IpcUnixDomainSocket();

                    using var connectTimeoutTokenSource = new CancellationTokenSource();
                    using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

                    try
                    {
                        connectTimeoutTokenSource.CancelAfter(IpcClientTimeoutMs);
                        await unixDomainSocket.ConnectAsync(new IpcUnixDomainSocketEndPoint(_ipcClientPath), token).ConfigureAwait(false);
                        retry = false;
                    }
                    catch (Exception)
                    {
                        unixDomainSocket?.Dispose();

                        if (connectTimeoutTokenSource.IsCancellationRequested)
                        {
                            Logger.LogDebug("No ipc stream connected, timing out.");

                            throw new TimeoutException();
                        }

                        Logger.LogDebug($"Failed connecting {_ipcClientPath}, wait {IpcClientRetryTimeoutMs} ms before retrying.");

                        // If we get an error (without hitting timeout above), most likely due to unavailable listener.
                        // Delay execution to prevent to rapid retry attempts.
                        await Task.Delay(IpcClientRetryTimeoutMs, token).ConfigureAwait(false);

                        if (IpcClientTimeoutMs != Timeout.Infinite)
                            throw;

                        retry = true;
                    }
                }
                while (retry);

                ipcClientStream = new ExposedSocketNetworkStream(unixDomainSocket, ownsSocket: true);
            }

            try
            {
                // ReversedDiagnosticsServer consumes advertise message, needs to be replayed back to ipc client stream. Use router process ID as representation.
                await IpcAdvertise.SerializeAsync(ipcClientStream, RuntimeInstanceId, (ulong)Process.GetCurrentProcess().Id, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Logger.LogDebug("Failed sending advertise message.");

                ipcClientStream?.Dispose();
                throw;
            }

            if (ipcClientStream != null)
                Logger.LogDebug("Successfully connected ipc stream.");

            return ipcClientStream;
        }

        public void CreatedNewServer(EndPoint localEP)
        {
            if (localEP is IPEndPoint ipEP)
                _tcpServerAddress = _tcpServerAddress.Replace(":0", string.Format(":{0}", ipEP.Port));
        }
    }

    internal class Router : IDisposable
    {
        readonly ILogger _logger;

        Stream _frontendStream = null;
        Stream _backendStream = null;

        Task _backendReadFrontendWriteTask = null;
        Task _frontendReadBackendWriteTask = null;

        CancellationTokenSource _cancelRouterTokenSource = null;

        bool _disposed = false;

        ulong _backendToFrontendByteTransfer;
        ulong _frontendToBackendByteTransfer;

        static int s_routerInstanceCount;

        public TaskCompletionSource<bool> RouterTaskCompleted { get; }

        public Router(Stream frontendStream, Stream backendStream, ILogger logger, ulong initBackendToFrontendByteTransfer = 0, ulong initFrontendToBackendByteTransfer = 0)
        {
            _logger = logger;

            _frontendStream = frontendStream;
            _backendStream = backendStream;

            _cancelRouterTokenSource = new CancellationTokenSource();

            RouterTaskCompleted = new TaskCompletionSource<bool>();

            _backendToFrontendByteTransfer = initBackendToFrontendByteTransfer;
            _frontendToBackendByteTransfer = initFrontendToBackendByteTransfer;

            Interlocked.Increment(ref s_routerInstanceCount);
        }

        public void Start()
        {
            if (_backendReadFrontendWriteTask != null || _frontendReadBackendWriteTask != null || _disposed)
                throw new InvalidOperationException();

            _backendReadFrontendWriteTask = BackendReadFrontendWrite(_cancelRouterTokenSource.Token);
            _frontendReadBackendWriteTask = FrontendReadBackendWrite(_cancelRouterTokenSource.Token);
        }

        public async void Stop()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Router));

            _cancelRouterTokenSource.Cancel();

            List<Task> runningTasks = new List<Task>();

            if (_backendReadFrontendWriteTask != null)
                runningTasks.Add(_backendReadFrontendWriteTask);

            if (_frontendReadBackendWriteTask != null)
                runningTasks.Add(_frontendReadBackendWriteTask);

            await Task.WhenAll(runningTasks.ToArray()).ConfigureAwait(false);

            _backendReadFrontendWriteTask?.Dispose();
            _frontendReadBackendWriteTask?.Dispose();

            RouterTaskCompleted?.TrySetResult(true);

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

                _cancelRouterTokenSource.Dispose();

                _backendStream?.Dispose();
                _frontendStream?.Dispose();

                _disposed = true;

                Interlocked.Decrement(ref s_routerInstanceCount);

                _logger.LogDebug($"Diposed stats: Backend->Frontend {_backendToFrontendByteTransfer} bytes, Frontend->Backend {_frontendToBackendByteTransfer} bytes.");
                _logger.LogDebug($"Active instances: {s_routerInstanceCount}");
            }
        }

        async Task BackendReadFrontendWrite(CancellationToken token)
        {
            try
            {
                byte[] buffer = new byte[1024];
                while (!token.IsCancellationRequested)
                {
                    _logger.LogDebug("Start reading bytes from backend.");

                    int bytesRead = await _backendStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

                    _logger.LogDebug($"Read {bytesRead} bytes from backend.");

                    // Check for end of stream indicating that remote end hung-up.
                    if (bytesRead == 0)
                    {
                        _logger.LogDebug("Backend hung up.");
                        break;
                    }

                    _backendToFrontendByteTransfer += (ulong)bytesRead;

                    _logger.LogDebug($"Start writing {bytesRead} bytes to frontend.");

                    await _frontendStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                    await _frontendStream.FlushAsync().ConfigureAwait(false);

                    _logger.LogDebug($"Wrote {bytesRead} bytes to frontend.");
                }
            }
            catch (Exception)
            {
                // Completing task will trigger dispose of instance and cleanup.
                // Faliure mainly consists of closed/disposed streams and cancelation requests.
                // Just make sure task gets complete, nothing more needs to be in response to these exceptions.
                _logger.LogDebug("Failed stream operation. Completing task.");
            }

            RouterTaskCompleted?.TrySetResult(true);
        }

        async Task FrontendReadBackendWrite(CancellationToken token)
        {
            try
            {
                byte[] buffer = new byte[1024];
                while (!token.IsCancellationRequested)
                {
                    _logger.LogDebug("Start reading bytes from frotend.");

                    int bytesRead = await _frontendStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

                    _logger.LogDebug($"Read {bytesRead} bytes from frontend.");

                    // Check for end of stream indicating that remote end hung-up.
                    if (bytesRead == 0)
                    {
                        _logger.LogDebug("Frontend hung up.");
                        break;
                    }

                    _frontendToBackendByteTransfer += (ulong)bytesRead;

                    _logger.LogDebug($"Start writing {bytesRead} bytes to backend.");

                    await _backendStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                    await _backendStream.FlushAsync().ConfigureAwait(false);

                    _logger.LogDebug($"Wrote {bytesRead} bytes to backend.");
                }
            }
            catch (Exception)
            {
                // Completing task will trigger dispose of instance and cleanup.
                // Faliure mainly consists of closed/disposed streams and cancelation requests.
                // Just make sure task gets complete, nothing more needs to be in response to these exceptions.
                _logger.LogDebug("Failed stream operation. Completing task.");
            }

            RouterTaskCompleted?.TrySetResult(true);
        }
    }
}
