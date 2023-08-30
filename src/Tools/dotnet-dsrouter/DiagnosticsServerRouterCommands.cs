// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.DiagnosticsServerRouter
{
    public class DiagnosticsServerRouterLauncher : DiagnosticsServerRouterRunner.ICallbacks
    {
        public CancellationToken CommandToken { get; set; }
        public bool SuspendProcess { get; set; }
        public bool ConnectMode { get; set; }
        public bool Verbose { get; set; }

        public void OnRouterStarted(string tcpAddress)
        {
            if (ProcessLauncher.Launcher.HasChildProc)
            {
                string diagnosticPorts = tcpAddress + (SuspendProcess ? ",suspend" : ",nosuspend") + (ConnectMode ? ",connect" : ",listen");
                if (ProcessLauncher.Launcher.ChildProc.StartInfo.Arguments.Contains("${DOTNET_DiagnosticPorts}", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessLauncher.Launcher.ChildProc.StartInfo.Arguments = ProcessLauncher.Launcher.ChildProc.StartInfo.Arguments.Replace("${DOTNET_DiagnosticPorts}", diagnosticPorts);
                    diagnosticPorts = "";
                }

                ProcessLauncher.Launcher.Start(diagnosticPorts, CommandToken, Verbose, Verbose);
            }
        }

        public void OnRouterStopped()
        {
            ProcessLauncher.Launcher.Cleanup();
        }
    }

    public class DiagnosticsServerRouterCommands
    {

        public DiagnosticsServerRouterCommands()
        {
        }

        // Common behavior for different commands used by CommonRunLoop
        internal abstract class SpecificRunnerBase
        {
            public DiagnosticsServerRouterLauncher Launcher { get; } = new DiagnosticsServerRouterLauncher();

            public LogLevel LogLevel { get; }
            // runners can override if necessary
            public virtual ILoggerFactory ConfigureLogging()
            {
                ILoggerFactory factory = LoggerFactory.Create(builder => {
                    builder.SetMinimumLevel(LogLevel);
                    builder.AddSimpleConsole(configure => {
                        configure.IncludeScopes = true;
                    });
                });
                return factory;
            }

            protected SpecificRunnerBase(LogLevel logLevel)
            {
                LogLevel = logLevel;
            }

            public abstract void ConfigureLauncher(CancellationToken cancellationToken);

            // The basic run loop: configure logging and the launcher, then create the router and run it until it exits or the user interrupts
            public async Task<int> CommonRunLoop(Func<ILogger, DiagnosticsServerRouterRunner.ICallbacks, CancellationTokenSource, Task<int>> createRouterTask, CancellationToken token)
            {
                using CancellationTokenSource cancelRouterTask = new();
                using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelRouterTask.Token);

                using ILoggerFactory loggerFactory = ConfigureLogging();

                ConfigureLauncher(token);

                int pid = Process.GetCurrentProcess().Id;

                ILogger logger = loggerFactory.CreateLogger($"dotnet-dsrouter-{pid}");

                logger.LogInformation($"Starting dotnet-dsrouter using pid={pid}");

                Task<int> routerTask = createRouterTask(logger, Launcher, linkedCancelToken);

                while (!linkedCancelToken.IsCancellationRequested)
                {
                    await Task.WhenAny(routerTask, Task.Delay(
                        250,
                        linkedCancelToken.Token)).ConfigureAwait(false);
                    if (routerTask.IsCompleted)
                    {
                        break;
                    }

                    if (!Console.IsInputRedirected && Console.KeyAvailable)
                    {
                        ConsoleKey cmd = Console.ReadKey(true).Key;
                        if (cmd == ConsoleKey.Q)
                        {
                            cancelRouterTask.Cancel();
                            break;
                        }
                    }
                }

                if (!routerTask.IsCompleted)
                {
                    cancelRouterTask.Cancel();
                }

                await Task.WhenAny(routerTask, Task.Delay(1000, CancellationToken.None)).ConfigureAwait(false);
                if (routerTask.IsCompleted)
                {
                    return routerTask.Result;
                }

                return 0;
            }
        }

        private sealed class IpcClientTcpServerRunner : SpecificRunnerBase
        {
            public IpcClientTcpServerRunner(LogLevel logLevel) : base(logLevel) { }

            public override void ConfigureLauncher(CancellationToken cancellationToken)
            {
                Launcher.SuspendProcess = true;
                Launcher.ConnectMode = true;
                Launcher.Verbose = LogLevel < LogLevel.Information;
                Launcher.CommandToken = cancellationToken;
            }

            public override ILoggerFactory ConfigureLogging()
            {
                ILoggerFactory factory = LoggerFactory.Create(builder => {
                    builder.SetMinimumLevel(LogLevel);
                    builder.AddConsole();
                });
                return factory;
            }
        }

        public async Task<int> RunIpcClientTcpServerRouter(CancellationToken token, string ipcClient, string tcpServer, int runtimeTimeout, string verbose, string forwardPort)
        {
            LogLevel logLevel = ParseLogLevel(verbose);

            checkLoopbackOnly(tcpServer, logLevel);

            IpcClientTcpServerRunner runner = new(logLevel);

            return await runner.CommonRunLoop((logger, launcherCallbacks, linkedCancelToken) => {
                NetServerRouterFactory.CreateInstanceDelegate tcpServerRouterFactory = ChooseTcpServerRouterFactory(forwardPort, logger);

                Task<int> routerTask = DiagnosticsServerRouterRunner.runIpcClientTcpServerRouter(linkedCancelToken.Token, ipcClient, tcpServer, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, tcpServerRouterFactory, logger, launcherCallbacks);
                return routerTask;
            }, token).ConfigureAwait(false);
        }

        private sealed class IpcServerTcpServerRunner : SpecificRunnerBase
        {
            public IpcServerTcpServerRunner(LogLevel logLevel) : base(logLevel) { }

            public override void ConfigureLauncher(CancellationToken cancellationToken)
            {
                Launcher.SuspendProcess = false;
                Launcher.ConnectMode = true;
                Launcher.Verbose = LogLevel < LogLevel.Information;
                Launcher.CommandToken = cancellationToken;
            }
        }

        public async Task<int> RunIpcServerTcpServerRouter(CancellationToken token, string ipcServer, string tcpServer, int runtimeTimeout, string verbose, string forwardPort)
        {
            LogLevel logLevel = ParseLogLevel(verbose);

            checkLoopbackOnly(tcpServer, logLevel);

            IpcServerTcpServerRunner runner = new(logLevel);

            return await runner.CommonRunLoop((logger, launcherCallbacks, linkedCancelToken) => {
                NetServerRouterFactory.CreateInstanceDelegate tcpServerRouterFactory = ChooseTcpServerRouterFactory(forwardPort, logger);

                if (string.IsNullOrEmpty(ipcServer))
                {
                    ipcServer = GetDefaultIpcServerPath(logger);
                }

                Task<int> routerTask = DiagnosticsServerRouterRunner.runIpcServerTcpServerRouter(linkedCancelToken.Token, ipcServer, tcpServer, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, tcpServerRouterFactory, logger, launcherCallbacks);
                return routerTask;
            }, token).ConfigureAwait(false);
        }

        private sealed class IpcServerTcpClientRunner : SpecificRunnerBase
        {
            public IpcServerTcpClientRunner(LogLevel logLevel) : base(logLevel) { }

            public override void ConfigureLauncher(CancellationToken cancellationToken)
            {
                Launcher.SuspendProcess = false;
                Launcher.ConnectMode = false;
                Launcher.Verbose = LogLevel < LogLevel.Information;
                Launcher.CommandToken = cancellationToken;
            }
        }

        public async Task<int> RunIpcServerTcpClientRouter(CancellationToken token, string ipcServer, string tcpClient, int runtimeTimeout, string verbose, string forwardPort)
        {
            IpcServerTcpClientRunner runner = new(ParseLogLevel(verbose));
            return await runner.CommonRunLoop((logger, launcherCallbacks, linkedCancelToken) => {
                TcpClientRouterFactory.CreateInstanceDelegate tcpClientRouterFactory = ChooseTcpClientRouterFactory(forwardPort, logger);

                if (string.IsNullOrEmpty(ipcServer))
                {
                    ipcServer = GetDefaultIpcServerPath(logger);
                }

                Task<int> routerTask = DiagnosticsServerRouterRunner.runIpcServerTcpClientRouter(linkedCancelToken.Token, ipcServer, tcpClient, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, tcpClientRouterFactory, logger, launcherCallbacks);
                return routerTask;
            }, token).ConfigureAwait(false);
        }

        private sealed class IpcClientTcpClientRunner : SpecificRunnerBase
        {
            public IpcClientTcpClientRunner(LogLevel logLevel) : base(logLevel) { }

            public override void ConfigureLauncher(CancellationToken cancellationToken)
            {
                Launcher.SuspendProcess = true;
                Launcher.ConnectMode = false;
                Launcher.Verbose = LogLevel < LogLevel.Information;
                Launcher.CommandToken = cancellationToken;
            }
        }

        public async Task<int> RunIpcClientTcpClientRouter(CancellationToken token, string ipcClient, string tcpClient, int runtimeTimeout, string verbose, string forwardPort)
        {
            IpcClientTcpClientRunner runner = new(ParseLogLevel(verbose));
            return await runner.CommonRunLoop((logger, launcherCallbacks, linkedCancelToken) => {
                TcpClientRouterFactory.CreateInstanceDelegate tcpClientRouterFactory = ChooseTcpClientRouterFactory(forwardPort, logger);

                Task<int> routerTask = DiagnosticsServerRouterRunner.runIpcClientTcpClientRouter(linkedCancelToken.Token, ipcClient, tcpClient, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, tcpClientRouterFactory, logger, launcherCallbacks);
                return routerTask;
            }, token).ConfigureAwait(false);
        }

        private sealed class IpcServerWebSocketServerRunner : SpecificRunnerBase
        {
            public IpcServerWebSocketServerRunner(LogLevel logLevel) : base(logLevel) { }

            public override void ConfigureLauncher(CancellationToken cancellationToken)
            {
                Launcher.SuspendProcess = false;
                Launcher.ConnectMode = true;
                Launcher.Verbose = LogLevel < LogLevel.Information;
                Launcher.CommandToken = cancellationToken;
            }
        }

        public async Task<int> RunIpcServerWebSocketServerRouter(CancellationToken token, string ipcServer, string webSocket, int runtimeTimeout, string verbose)
        {
            IpcServerWebSocketServerRunner runner = new(ParseLogLevel(verbose));

            WebSocketServer.WebSocketServerImpl server = new(runner.LogLevel);

            NETCore.Client.WebSocketServer.WebSocketServerProvider.SetProvider(() => server);

            try
            {
                Task _ = Task.Run(() => server.StartServer(webSocket, token));

                return await runner.CommonRunLoop((logger, launcherCallbacks, linkedCancelToken) => {
                    NetServerRouterFactory.CreateInstanceDelegate webSocketServerRouterFactory = WebSocketServerRouterFactory.CreateDefaultInstance;

                    if (string.IsNullOrEmpty(ipcServer))
                    {
                        ipcServer = GetDefaultIpcServerPath(logger);
                    }

                    Task<int> routerTask = DiagnosticsServerRouterRunner.runIpcServerTcpServerRouter(linkedCancelToken.Token, ipcServer, webSocket, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, webSocketServerRouterFactory, logger, launcherCallbacks);
                    return routerTask;
                }, token).ConfigureAwait(false);
            }
            finally
            {
                await server.StopServer(token).ConfigureAwait(false);
            }
        }

        private sealed class IpcClientWebSocketServerRunner : SpecificRunnerBase
        {
            public IpcClientWebSocketServerRunner(LogLevel logLevel) : base(logLevel) { }

            public override void ConfigureLauncher(CancellationToken cancellationToken)
            {
                Launcher.SuspendProcess = true;
                Launcher.ConnectMode = true;
                Launcher.Verbose = LogLevel < LogLevel.Information;
                Launcher.CommandToken = cancellationToken;
            }
        }

        public async Task<int> RunIpcClientWebSocketServerRouter(CancellationToken token, string ipcClient, string webSocket, int runtimeTimeout, string verbose)
        {
            IpcClientWebSocketServerRunner runner = new(ParseLogLevel(verbose));

            WebSocketServer.WebSocketServerImpl server = new(runner.LogLevel);

            NETCore.Client.WebSocketServer.WebSocketServerProvider.SetProvider(() => server);

            try
            {
                Task _ = Task.Run(() => server.StartServer(webSocket, token));

                return await runner.CommonRunLoop((logger, launcherCallbacks, linkedCancelToken) => {
                    NetServerRouterFactory.CreateInstanceDelegate webSocketServerRouterFactory = WebSocketServerRouterFactory.CreateDefaultInstance;

                    Task<int> routerTask = DiagnosticsServerRouterRunner.runIpcClientTcpServerRouter(linkedCancelToken.Token, ipcClient, webSocket, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, webSocketServerRouterFactory, logger, launcherCallbacks);
                    return routerTask;
                }, token).ConfigureAwait(false);
            }
            finally
            {
                await server.StopServer(token).ConfigureAwait(false);
            }
        }

        public async Task<int> RunIpcServerIOSSimulatorRouter(CancellationToken token, int runtimeTimeout, string verbose, bool info)
        {
            if (info)
            {
                logRouterUsageInfo("ios simulator", "127.0.0.1:9000", false);
            }

            return await RunIpcServerTcpClientRouter(token, "", "127.0.0.1:9000", runtimeTimeout, verbose, "").ConfigureAwait(false);
        }

        public async Task<int> RunIpcServerIOSRouter(CancellationToken token, int runtimeTimeout, string verbose, bool info)
        {
            if (info)
            {
                logRouterUsageInfo("ios device", "127.0.0.1:9000", true);
            }

            return await RunIpcServerTcpClientRouter(token, "", "127.0.0.1:9000", runtimeTimeout, verbose, "iOS").ConfigureAwait(false);
        }

        public async Task<int> RunIpcServerAndroidEmulatorRouter(CancellationToken token, int runtimeTimeout, string verbose, bool info)
        {
            if (info)
            {
                logRouterUsageInfo("android emulator", "10.0.2.2:9000", false);
            }

            return await RunIpcServerTcpServerRouter(token, "", "127.0.0.1:9000", runtimeTimeout, verbose, "").ConfigureAwait(false);
        }

        public async Task<int> RunIpcServerAndroidRouter(CancellationToken token, int runtimeTimeout, string verbose, bool info)
        {
            if (info)
            {
                logRouterUsageInfo("android device", "127.0.0.1:9000", false);
            }

            return await RunIpcServerTcpServerRouter(token, "", "127.0.0.1:9000", runtimeTimeout, verbose, "Android").ConfigureAwait(false);
        }

        private static string GetDefaultIpcServerPath(ILogger logger)
        {
            string path = string.Empty;
            int processId = Process.GetCurrentProcess().Id;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path = $"dotnet-diagnostic-dsrouter-{processId}";
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
                path = Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-dsrouter-{processId}-{(long)diff.TotalSeconds}-socket");
            }

            logger?.LogDebug($"Using default IPC server path, {path}.");
            logger?.LogDebug($"Attach to default dotnet-dsrouter IPC server using --process-id {processId} diagnostic tooling argument.");

            return path;
        }

        private static TcpClientRouterFactory.CreateInstanceDelegate ChooseTcpClientRouterFactory(string forwardPort, ILogger logger)
        {
            TcpClientRouterFactory.CreateInstanceDelegate tcpClientRouterFactory = TcpClientRouterFactory.CreateDefaultInstance;
            if (!string.IsNullOrEmpty(forwardPort))
            {
                if (string.Equals(forwardPort, "android", StringComparison.OrdinalIgnoreCase))
                {
                    tcpClientRouterFactory = ADBTcpClientRouterFactory.CreateADBInstance;
                }
                else if (string.Equals(forwardPort, "ios", StringComparison.OrdinalIgnoreCase))
                {
                    tcpClientRouterFactory = USBMuxTcpClientRouterFactory.CreateUSBMuxInstance;
                }
                else
                {
                    logger.LogError($"Unknown port forwarding argument, {forwardPort}. Ignoring --forward-port argument.");
                }
            }
            return tcpClientRouterFactory;
        }

        private static NetServerRouterFactory.CreateInstanceDelegate ChooseTcpServerRouterFactory(string forwardPort, ILogger logger)
        {
            NetServerRouterFactory.CreateInstanceDelegate tcpServerRouterFactory = TcpServerRouterFactory.CreateDefaultInstance;
            if (!string.IsNullOrEmpty(forwardPort))
            {
                if (string.Equals(forwardPort, "android", StringComparison.OrdinalIgnoreCase))
                {
                    tcpServerRouterFactory = ADBTcpServerRouterFactory.CreateADBInstance;
                }
                else
                {
                    logger.LogError($"Unknown port forwarding argument, {forwardPort}. Only Android port fowarding is supported for TcpServer mode. Ignoring --forward-port argument.");
                }
            }
            return tcpServerRouterFactory;
        }

        private static LogLevel ParseLogLevel(string verbose)
        {
            LogLevel logLevel = LogLevel.Information;
            if (string.Equals(verbose, "none", StringComparison.OrdinalIgnoreCase))
            {
                logLevel = LogLevel.None;
            }
            else if (string.Equals(verbose, "critical", StringComparison.OrdinalIgnoreCase))
            {
                logLevel = LogLevel.Critical;
            }
            else if (string.Equals(verbose, "error", StringComparison.OrdinalIgnoreCase))
            {
                logLevel = LogLevel.Error;
            }
            else if (string.Equals(verbose, "warning", StringComparison.OrdinalIgnoreCase))
            {
                logLevel = LogLevel.Warning;
            }
            else if (string.Equals(verbose, "info", StringComparison.OrdinalIgnoreCase))
            {
                logLevel = LogLevel.Information;
            }
            else if (string.Equals(verbose, "debug", StringComparison.OrdinalIgnoreCase))
            {
                logLevel = LogLevel.Debug;
            }
            else if (string.Equals(verbose, "trace", StringComparison.OrdinalIgnoreCase))
            {
                logLevel = LogLevel.Trace;
            }

            return logLevel;
        }

        private static void logRouterUsageInfo(string deviceName, string deviceTcpIpAddress, bool deviceListenMode)
        {
            StringBuilder message = new();

            string listenMode = deviceListenMode ? "listen" : "connect";
            int pid = Process.GetCurrentProcess().Id;

            message.AppendLine($"How to connect current dotnet-dsrouter pid={pid} with {deviceName} and diagnostics tooling.");
            message.AppendLine($"Start an application on {deviceName} with ONE of the following environment variables set:");
            message.AppendLine("[Default Tracing]");
            message.AppendLine($"DOTNET_DiagnosticPorts={deviceTcpIpAddress},nosuspend,{listenMode}");
            message.AppendLine("[Startup Tracing]");
            message.AppendLine($"DOTNET_DiagnosticPorts={deviceTcpIpAddress},suspend,{listenMode}");
            message.AppendLine($"Run diagnotic tool connecting application on {deviceName} through dotnet-dsrouter pid={pid}:");
            message.AppendLine($"dotnet-trace collect -p {pid}");
            message.AppendLine($"See https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dsrouter for additional details and examples.");

            ConsoleColor currentColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message.ToString());
            Console.ForegroundColor = currentColor;
        }

        private static void checkLoopbackOnly(string tcpServer, LogLevel logLevel)
        {
            if (logLevel != LogLevel.None && !string.IsNullOrEmpty(tcpServer) && !DiagnosticsServerRouterRunner.isLoopbackOnly(tcpServer))
            {
                StringBuilder message = new();

                message.Append("WARNING: Binding tcp server endpoint to anything except loopback interface ");
                message.Append("(localhost, 127.0.0.1 or [::1]) is NOT recommended. Any connections towards ");
                message.Append("tcp server endpoint will be unauthenticated and unencrypted. This component ");
                message.Append("is intended for development use and should only be run in development and ");
                message.Append("testing environments.");
                message.AppendLine();

                ConsoleColor currentColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(message.ToString());
                Console.ForegroundColor = currentColor;
            }
        }
    }
}
