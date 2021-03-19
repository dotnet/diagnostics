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
    // TODO: Add support for IPC Server <--> TCP Server proxy, RunISTSProxy.
    public class DiagnosticServerProxyCommands
    {
        public DiagnosticServerProxyCommands()
        {
        }

        public async Task<int> RunIpcClientTcpServerProxy(CancellationToken token, string ipcClient, string tcpServer, bool autoShutdown, bool debug)
        {
            checkLoopbackOnly(tcpServer);

            using CancellationTokenSource cancelProxyTask = new CancellationTokenSource();
            using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelProxyTask.Token);

            var proxyTask = DiagnosticServerProxyRunner.runIpcClientTcpServerProxy(linkedCancelToken.Token, ipcClient, tcpServer, autoShutdown, debug);

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

        public async Task<int> RunIpcServerTcpServerProxy(CancellationToken token, string ipcServer, string tcpServer, bool autoShutdown, bool debug)
        {
            checkLoopbackOnly(tcpServer);

            using CancellationTokenSource cancelProxyTask = new CancellationTokenSource();
            using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelProxyTask.Token);

            var proxyTask = DiagnosticServerProxyRunner.runIpcServerTcpServerProxy(linkedCancelToken.Token, ipcServer, tcpServer, autoShutdown, debug);

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
            if (!DiagnosticServerProxyRunner.isLoopbackOnly(tcpServer))
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
