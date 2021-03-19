// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.Diagnostics.NETCore.Client
{
    // <summary>
    // Class used to run different flavours of Diagnostic Server proxies.
    // </summary>
    internal class DiagnosticServerProxyRunner
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
}
