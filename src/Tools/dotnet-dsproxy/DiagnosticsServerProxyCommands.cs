// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.DSProxy
{

    class DiagnosticsServerProxyConsoleLogger : DiagnosticsServerProxyLogger
    {
        LogLevel _logLevel;

        public DiagnosticsServerProxyConsoleLogger(bool verbose)
        {
            _logLevel = verbose ? LogLevel.Debug : LogLevel.Info;
        }

        public override void LogError(string msg)
        {
            if (_logLevel >= LogLevel.Info)
                Console.WriteLine("ERROR: " + msg);
        }

        public override void LogWarning(string msg)
        {
            if (_logLevel >= LogLevel.Info)
                Console.WriteLine("WARNING: " + msg);
        }

        public override void LogInfo(string msg)
        {
            if (_logLevel >= LogLevel.Info)
                Console.WriteLine(msg);
        }

        public override void LogDebug(string msg)
        {
            if (_logLevel == LogLevel.Debug)
                Console.WriteLine(msg);
        }

    }

    public class DiagnosticsServerProxyCommands
    {
        public DiagnosticsServerProxyCommands()
        {
        }

        public async Task<int> RunIpcClientTcpServerProxy(CancellationToken token, string ipcClient, string tcpServer, int runtimeTimeout, bool verbose)
        {
            checkLoopbackOnly(tcpServer);

            using CancellationTokenSource cancelProxyTask = new CancellationTokenSource();
            using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelProxyTask.Token);

            var proxyTask = DiagnosticsServerProxyRunner.runIpcClientTcpServerProxy(linkedCancelToken.Token, ipcClient, tcpServer, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, new DiagnosticsServerProxyConsoleLogger(verbose));

            while (!linkedCancelToken.IsCancellationRequested)
            {
                await Task.WhenAny(proxyTask, Task.Delay(250)).ConfigureAwait(false);
                if (proxyTask.IsCompleted)
                    break;

                if (Console.KeyAvailable)
                {
                    ConsoleKey cmd = Console.ReadKey(true).Key;
                    if (cmd == ConsoleKey.Q)
                    {
                        cancelProxyTask.Cancel();
                        break;
                    }
                }
            }

            return proxyTask.Result;
        }

        public async Task<int> RunIpcServerTcpServerProxy(CancellationToken token, string ipcServer, string tcpServer, int runtimeTimeout, bool verbose)
        {
            checkLoopbackOnly(tcpServer);

            using CancellationTokenSource cancelProxyTask = new CancellationTokenSource();
            using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelProxyTask.Token);

            var proxyTask = DiagnosticsServerProxyRunner.runIpcServerTcpServerProxy(linkedCancelToken.Token, ipcServer, tcpServer, runtimeTimeout == Timeout.Infinite ? runtimeTimeout : runtimeTimeout * 1000, new DiagnosticsServerProxyConsoleLogger(verbose));

            while (!linkedCancelToken.IsCancellationRequested)
            {
                await Task.WhenAny(proxyTask, Task.Delay(250)).ConfigureAwait(false);
                if (proxyTask.IsCompleted)
                    break;

                if (Console.KeyAvailable)
                {
                    ConsoleKey cmd = Console.ReadKey(true).Key;
                    if (cmd == ConsoleKey.Q)
                    {
                        cancelProxyTask.Cancel();
                        break;
                    }
                }
            }

            return proxyTask.Result;
        }

        static void checkLoopbackOnly(string tcpServer)
        {
            if (!DiagnosticsServerProxyRunner.isLoopbackOnly(tcpServer))
            {
                StringBuilder message = new StringBuilder();

                message.Append("WARNING: Binding tcp server endpoint to anything except loopback interface ");
                message.Append("(localhost, 127.0.0.1 or [::1]) is NOT recommended. Any connections towards ");
                message.Append("tcp server endpoint will be unauthenticated and unencrypted. This component ");
                message.Append("is intented for development use and should only be run in development and ");
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
