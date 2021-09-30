// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Tools.DiagnosticsServerRouter
{
    public class DiagnosticsServerRouterLauncher : DiagnosticsServerRouterRunner.Callbacks
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
        public static DiagnosticsServerRouterLauncher Launcher { get; } = new DiagnosticsServerRouterLauncher();

        public DiagnosticsServerRouterCommands()
        {
        }

        public async Task<int> RunIpcClientTcpServerRouter(CancellationToken token, string ipcClient, string tcpServer, int runtimeTimeout, string verbose, string forwardPort)
        {
            checkLoopbackOnly(tcpServer);

            using CancellationTokenSource cancelRouterTask = new CancellationTokenSource();
            using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelRouterTask.Token);

            LogLevel logLevel = LogLevel.Information;
            if (string.Compare(verbose, "debug", StringComparison.OrdinalIgnoreCase) == 0)
                logLevel = LogLevel.Debug;
            else if (string.Compare(verbose, "trace", StringComparison.OrdinalIgnoreCase) == 0)
                logLevel = LogLevel.Trace;

            using var factory = new LoggerFactory();
            factory.AddConsole(logLevel, false);

            Launcher.SuspendProcess = true;
            Launcher.ConnectMode = true;
            Launcher.Verbose = logLevel != LogLevel.Information;
            Launcher.CommandToken = token;

            var logger = factory.CreateLogger("dotnet-dsrouter");

            TcpServerRouterFactory.CreateInstanceDelegate tcpServerRouterFactory = TcpServerRouterFactory.CreateDefaultInstance;
            if (!string.IsNullOrEmpty(forwardPort))
            {
                if (string.Compare(forwardPort, "android", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    tcpServerRouterFactory = ADBTcpServerRouterFactory.CreateADBInstance;
                }
                else
                {
                    logger.LogError($"Unknown port forwarding argument, {forwardPort}. Only Android port fowarding is supported for TcpServer mode. Ignoring --forward-port argument.");
                }
            }

            var routerTask = DiagnosticsServerRouterRunner.runIpcClientTcpServerRouter(linkedCancelToken.Token, ipcClient, tcpServer, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, tcpServerRouterFactory, logger, Launcher);

            while (!linkedCancelToken.IsCancellationRequested)
            {
                await Task.WhenAny(routerTask, Task.Delay(250)).ConfigureAwait(false);
                if (routerTask.IsCompleted)
                    break;

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

            return routerTask.Result;
        }

        public async Task<int> RunIpcServerTcpServerRouter(CancellationToken token, string ipcServer, string tcpServer, int runtimeTimeout, string verbose, string forwardPort)
        {
            checkLoopbackOnly(tcpServer);

            using CancellationTokenSource cancelRouterTask = new CancellationTokenSource();
            using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelRouterTask.Token);

            LogLevel logLevel = LogLevel.Information;
            if (string.Compare(verbose, "debug", StringComparison.OrdinalIgnoreCase) == 0)
                logLevel = LogLevel.Debug;
            else if (string.Compare(verbose, "trace", StringComparison.OrdinalIgnoreCase) == 0)
                logLevel = LogLevel.Trace;

            using var factory = new LoggerFactory();
            factory.AddConsole(logLevel, false);

            Launcher.SuspendProcess = false;
            Launcher.ConnectMode = true;
            Launcher.Verbose = logLevel != LogLevel.Information;
            Launcher.CommandToken = token;

            var logger = factory.CreateLogger("dotnet-dsrouter");

            TcpServerRouterFactory.CreateInstanceDelegate tcpServerRouterFactory = TcpServerRouterFactory.CreateDefaultInstance;
            if (!string.IsNullOrEmpty(forwardPort))
            {
                if (string.Compare(forwardPort, "android", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    tcpServerRouterFactory = ADBTcpServerRouterFactory.CreateADBInstance;
                }
                else
                {
                    logger.LogError($"Unknown port forwarding argument, {forwardPort}. Only Android port fowarding is supported for TcpServer mode. Ignoring --forward-port argument.");
                }
            }

            if (string.IsNullOrEmpty(ipcServer))
                ipcServer = GetDefaultIpcServerPath(logger);

            var routerTask = DiagnosticsServerRouterRunner.runIpcServerTcpServerRouter(linkedCancelToken.Token, ipcServer, tcpServer, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, tcpServerRouterFactory, logger, Launcher);

            while (!linkedCancelToken.IsCancellationRequested)
            {
                await Task.WhenAny(routerTask, Task.Delay(250)).ConfigureAwait(false);
                if (routerTask.IsCompleted)
                    break;

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

            return routerTask.Result;
        }

        public async Task<int> RunIpcServerTcpClientRouter(CancellationToken token, string ipcServer, string tcpClient, int runtimeTimeout, string verbose, string forwardPort)
        {
            using CancellationTokenSource cancelRouterTask = new CancellationTokenSource();
            using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelRouterTask.Token);

            LogLevel logLevel = LogLevel.Information;
            if (string.Compare(verbose, "debug", StringComparison.OrdinalIgnoreCase) == 0)
                logLevel = LogLevel.Debug;
            else if (string.Compare(verbose, "trace", StringComparison.OrdinalIgnoreCase) == 0)
                logLevel = LogLevel.Trace;

            using var factory = new LoggerFactory();
            factory.AddConsole(logLevel, false);

            Launcher.SuspendProcess = false;
            Launcher.ConnectMode = false;
            Launcher.Verbose = logLevel != LogLevel.Information;
            Launcher.CommandToken = token;

            var logger = factory.CreateLogger("dotnet-dsrouter");

            TcpClientRouterFactory.CreateInstanceDelegate tcpClientRouterFactory = TcpClientRouterFactory.CreateDefaultInstance;
            if (!string.IsNullOrEmpty(forwardPort))
            {
                if (string.Compare(forwardPort, "android", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    tcpClientRouterFactory = ADBTcpClientRouterFactory.CreateADBInstance;
                }
                else if (string.Compare(forwardPort, "ios", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    tcpClientRouterFactory = USBMuxTcpClientRouterFactory.CreateUSBMuxInstance;
                }
                else
                {
                    logger.LogError($"Unknown port forwarding argument, {forwardPort}. Ignoring --forward-port argument.");
                }
            }

            if (string.IsNullOrEmpty(ipcServer))
                ipcServer = GetDefaultIpcServerPath(logger);

            var routerTask = DiagnosticsServerRouterRunner.runIpcServerTcpClientRouter(linkedCancelToken.Token, ipcServer, tcpClient, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, tcpClientRouterFactory, logger, Launcher);

            while (!linkedCancelToken.IsCancellationRequested)
            {
                await Task.WhenAny(routerTask, Task.Delay(250)).ConfigureAwait(false);
                if (routerTask.IsCompleted)
                    break;

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

            return routerTask.Result;
        }

        public async Task<int> RunIpcClientTcpClientRouter(CancellationToken token, string ipcClient, string tcpClient, int runtimeTimeout, string verbose, string forwardPort)
        {
            using CancellationTokenSource cancelRouterTask = new CancellationTokenSource();
            using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelRouterTask.Token);

            LogLevel logLevel = LogLevel.Information;
            if (string.Compare(verbose, "debug", StringComparison.OrdinalIgnoreCase) == 0)
                logLevel = LogLevel.Debug;
            else if (string.Compare(verbose, "trace", StringComparison.OrdinalIgnoreCase) == 0)
                logLevel = LogLevel.Trace;

            using var factory = new LoggerFactory();
            factory.AddConsole(logLevel, false);

            Launcher.SuspendProcess = true;
            Launcher.ConnectMode = false;
            Launcher.Verbose = logLevel != LogLevel.Information;
            Launcher.CommandToken = token;

            var logger = factory.CreateLogger("dotnet-dsrouter");

            TcpClientRouterFactory.CreateInstanceDelegate tcpClientRouterFactory = TcpClientRouterFactory.CreateDefaultInstance;
            if (!string.IsNullOrEmpty(forwardPort))
            {
                if (string.Compare(forwardPort, "android", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    tcpClientRouterFactory = ADBTcpClientRouterFactory.CreateADBInstance;
                }
                else if (string.Compare(forwardPort, "ios", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    tcpClientRouterFactory = USBMuxTcpClientRouterFactory.CreateUSBMuxInstance;
                }
                else
                {
                    logger.LogError($"Unknown port forwarding argument, {forwardPort}. Ignoring --forward-port argument.");
                }
            }

            var routerTask = DiagnosticsServerRouterRunner.runIpcClientTcpClientRouter(linkedCancelToken.Token, ipcClient, tcpClient, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, tcpClientRouterFactory, logger, Launcher);

            while (!linkedCancelToken.IsCancellationRequested)
            {
                await Task.WhenAny(routerTask, Task.Delay(250)).ConfigureAwait(false);
                if (routerTask.IsCompleted)
                    break;

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

            return routerTask.Result;
        }

        static string GetDefaultIpcServerPath(ILogger logger)
        {
            int processId = Process.GetCurrentProcess().Id;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var path = Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-{processId}");
                if (File.Exists(path))
                {
                    logger?.LogWarning($"Default IPC server path, {path}, already in use. To disable default diagnostics for dotnet-dsrouter, set COMPlus_EnableDiagnostics=0 and re-run.");

                    path = Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-dsrouter-{processId}");
                    logger?.LogWarning($"Fallback using none default IPC server path, {path}.");
                }

                return path.Substring(PidIpcEndpoint.IpcRootPath.Length);
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

                var path = Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-{processId}-{(long)diff.TotalSeconds}-socket");
                if (Directory.GetFiles(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-{processId}-*-socket").Length != 0)
                {
                    logger?.LogWarning($"Default IPC server path, {Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-{processId}-*-socket")}, already in use. To disable default diagnostics for dotnet-dsrouter, set COMPlus_EnableDiagnostics=0 and re-run.");

                    path = Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-dsrouter-{processId}-{(long)diff.TotalSeconds}-socket");
                    logger?.LogWarning($"Fallback using none default IPC server path, {path}.");
                }

                return path;
            }
        }

        static void checkLoopbackOnly(string tcpServer)
        {
            if (!string.IsNullOrEmpty(tcpServer) && !DiagnosticsServerRouterRunner.isLoopbackOnly(tcpServer))
            {
                StringBuilder message = new StringBuilder();

                message.Append("WARNING: Binding tcp server endpoint to anything except loopback interface ");
                message.Append("(localhost, 127.0.0.1 or [::1]) is NOT recommended. Any connections towards ");
                message.Append("tcp server endpoint will be unauthenticated and unencrypted. This component ");
                message.Append("is intended for development use and should only be run in development and ");
                message.Append("testing environments.");
                message.AppendLine();

                var currentColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(message.ToString());
                Console.ForegroundColor = currentColor;
            }
        }
    }
}
