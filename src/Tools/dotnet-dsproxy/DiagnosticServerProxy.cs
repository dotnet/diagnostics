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
    public class DiagnosticServerProxy
    {
        bool _verboseLogging;

        public DiagnosticServerProxy()
        {
        }

        static bool IsConnectedProxyDead(ConnectedProxy connectedProxy)
        {
            bool isRunning = connectedProxy.IsRunning && !connectedProxy.ProxyTaskCompleted.Task.IsCompleted;
            if (!isRunning)
                connectedProxy.Dispose();
            return !isRunning;
        }

        async Task<int> internalRunICTSProxy(CancellationToken token, string ipcClient, string tcpServer, bool autoShutdown)
        {
            List<Task> runningTasks = new List<Task>();
            List<ConnectedProxy> runningProxies = new List<ConnectedProxy>();
            var proxyFactory = new ClientServerICTSProxyFactory(ipcClient, tcpServer, _verboseLogging);

            Console.WriteLine($"DiagnosticServerProxy: Starting IPC client <--> TCP server using IPC client endpoint=\"{ipcClient}\" and TCP server endpoint=\"{tcpServer}\".");

            try
            {
                proxyFactory.Start();
                while (!token.IsCancellationRequested)
                {
                    Task<ConnectedProxy> connectedProxyTask = null;
                    ConnectedProxy connectedProxy = null;

                    try
                    {
                        connectedProxyTask = proxyFactory.ConnectProxyAsync(token, new TaskCompletionSource());

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

                        // Timing out on accepting new streams could mean that either the client holds an open connection
                        // alive (but currently not using it), or we have a dead server endpoint. If there are no running
                        // proxies we assume a dead server endpoint. Reset current server endpoint and see if we get
                        // reconnect using same or different runtime instance.
                        if (ex is ServerStreamConnectTimeoutException && runningProxies.Count == 0)
                        {
                            if (autoShutdown || _verboseLogging)
                                Console.WriteLine("DiagnosticServerProxy: No server stream available before timeout.");

                            proxyFactory.Reset();
                        }

                        // Timing out on accepting a new runtime connection means there is no runtime alive.
                        // Shutdown proxy to prevent instances to outlive runtime process (if auto shutdown is enabled).
                        if (ex is RuntimeConnectTimeoutException)
                        {
                            if (autoShutdown || _verboseLogging)
                                Console.WriteLine("DiagnosticServerProxy: No server stream available before timeout.");

                            if (autoShutdown)
                            {
                                Console.WriteLine("DiagnosticServerProxy: Starting automatic server shutdown.");
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DiagnosticServerProxy: Shutting down due to error: {ex.Message}");
            }
            finally
            {
                runningProxies.RemoveAll(IsConnectedProxyDead);
                runningProxies.Clear();

                await proxyFactory?.Stop();

                Console.WriteLine("DiagnosticServerProxy: Stopped.");
            }
            return 0;
        }

        public async Task<int> RunICTSProxy(CancellationToken token, string ipcClient, string tcpServer, bool autoShutdown, bool debug)
        {
            CancellationTokenSource cancelProxyTask = new CancellationTokenSource();
            CancellationTokenSource linkedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(token, cancelProxyTask.Token);

            _verboseLogging = debug;

            var proxyTask = internalRunICTSProxy(linkedCancelToken.Token, ipcClient, tcpServer, autoShutdown);

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
