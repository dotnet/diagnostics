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
    // Class used to run different flavours of Diagnostics Server routers.
    // </summary>
    internal class DiagnosticsServerRouterRunner
    {
        public static async Task<int> runIpcClientTcpServerRouter(CancellationToken token, string ipcClient, string tcpServer, int runtimeTimeoutMS, DiagnosticsServerRouterLogger logger)
        {
            logger.LogInfo($"Starting IPC client ({ipcClient}) <--> TCP server ({tcpServer}) router.");
            return await runRouter(token, new IpcClientTcpServerRouter(ipcClient, tcpServer, runtimeTimeoutMS, logger)).ConfigureAwait(false);
        }

        public static async Task<int> runIpcServerTcpServerRouter(CancellationToken token, string ipcServer, string tcpServer, int runtimeTimeoutMS, DiagnosticsServerRouterLogger logger)
        {
            if (string.IsNullOrEmpty(ipcServer))
                ipcServer = IpcServerTcpServerRouter.GetDefaultIpcServerPath();

            logger.LogInfo($"Starting IPC server ({ipcServer}) <--> TCP server ({tcpServer}) router.");
            return await runRouter(token, new IpcServerTcpServerRouter(ipcServer, tcpServer, runtimeTimeoutMS, logger)).ConfigureAwait(false);
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

        async static Task<int> runRouter(CancellationToken token, DiagnosticsServerRouter router)
        {
            List<Task> runningTasks = new List<Task>();
            List<ConnectedRouter> runningRouters = new List<ConnectedRouter>();

            try
            {
                router.Start();
                while (!token.IsCancellationRequested)
                {
                    Task<ConnectedRouter> connectedRouterTask = null;
                    ConnectedRouter connectedRouter = null;

                    try
                    {
                        connectedRouterTask = router.ConnectRouterAsync(token);

                        do
                        {
                            // Search list and clean up dead router instances before continue waiting on new instances.
                            runningRouters.RemoveAll(IsConnectedRouterDead);

                            runningTasks.Clear();
                            foreach (var runningRouter in runningRouters)
                                runningTasks.Add(runningRouter.RouterTaskCompleted.Task);
                            runningTasks.Add(connectedRouterTask);
                        }
                        while (await Task.WhenAny(runningTasks.ToArray()).ConfigureAwait(false) != connectedRouterTask);

                        if (connectedRouterTask.IsFaulted || connectedRouterTask.IsCanceled)
                        {
                            //Throw original exception.
                            connectedRouterTask.GetAwaiter().GetResult();
                        }

                        if (connectedRouterTask.IsCompleted)
                        {
                            connectedRouter = connectedRouterTask.Result;
                            connectedRouter.Start();

                            // Add to list of running router instances.
                            runningRouters.Add(connectedRouter);
                            connectedRouter = null;
                        }

                        connectedRouterTask.Dispose();
                        connectedRouterTask = null;
                    }
                    catch (Exception ex)
                    {
                        connectedRouter?.Dispose();
                        connectedRouter = null;

                        connectedRouterTask?.Dispose();
                        connectedRouterTask = null;

                        // Timing out on accepting new streams could mean that either the frontend holds an open connection
                        // alive (but currently not using it), or we have a dead backend. If there are no running
                        // routers we assume a dead backend. Reset current backend endpoint and see if we get
                        // reconnect using same or different runtime instance.
                        if (ex is BackendStreamConnectTimeoutException && runningRouters.Count == 0)
                        {
                            router.Logger.LogDebug("No backend stream available before timeout.");
                            router.Reset();
                        }

                        // Timing out on accepting a new runtime connection means there is no runtime alive.
                        // Shutdown router to prevent instances to outlive runtime process (if auto shutdown is enabled).
                        if (ex is RuntimeConnectTimeoutException)
                        {
                            router.Logger.LogInfo("No runtime connected before timeout.");
                            router.Logger.LogInfo("Starting automatic shutdown.");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                router.Logger.LogInfo($"Shutting down due to error: {ex.Message}");
            }
            finally
            {
                if (token.IsCancellationRequested)
                    router.Logger.LogInfo("Shutting down due to cancelation request.");

                runningRouters.RemoveAll(IsConnectedRouterDead);
                runningRouters.Clear();

                await router?.Stop();

                router.Logger.LogInfo("Router stopped.");
            }
            return 0;
        }

        static bool IsConnectedRouterDead(ConnectedRouter connectedRouter)
        {
            bool isRunning = connectedRouter.IsRunning && !connectedRouter.RouterTaskCompleted.Task.IsCompleted;
            if (!isRunning)
                connectedRouter.Dispose();
            return !isRunning;
        }
    }
}
