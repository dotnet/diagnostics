// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Common.Utils;
using System.Linq;
using System.Diagnostics;

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
            using CancellationTokenSource cancelProxyTask = new CancellationTokenSource();
            using CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelProxyTask.Token);

            var proxyTask = DiagnosticServerProxyFactory.runIpcClientTcpServerProxy(linkedCancelToken.Token, ipcClient, tcpServer, autoShutdown, debug);

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
    }
}
