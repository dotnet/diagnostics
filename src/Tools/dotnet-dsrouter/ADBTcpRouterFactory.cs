// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Tools.DiagnosticsServerRouter
{
    internal static class ADBCommandExec
    {
        public static bool AdbAddPortForward(int port, ILogger logger)
        {
            bool ownsPortForward = false;
            if (!RunAdbCommandInternal($"forward --list", $"tcp:{port}", 0, logger))
            {
                ownsPortForward = RunAdbCommandInternal($"forward tcp:{port} tcp:{port}", "", 0, logger);
                if (!ownsPortForward)
                {
                    logger?.LogError($"Failed setting up port forward for tcp:{port}.");
                }
            }
            return ownsPortForward;
        }

        public static bool AdbAddPortReverse(int port, ILogger logger)
        {
            bool ownsPortForward = false;
            if (!RunAdbCommandInternal($"reverse --list", $"tcp:{port}", 0, logger))
            {
                ownsPortForward = RunAdbCommandInternal($"reverse tcp:{port} tcp:{port}", "", 0, logger);
                if (!ownsPortForward)
                {
                    logger?.LogError($"Failed setting up port forward for tcp:{port}.");
                }
            }
            return ownsPortForward;
        }

        public static void AdbRemovePortForward(int port, bool ownsPortForward, ILogger logger)
        {
            if (ownsPortForward)
            {
                if (!RunAdbCommandInternal($"forward --remove tcp:{port}", "", 0, logger))
                {
                    logger?.LogError($"Failed removing port forward for tcp:{port}.");
                }
            }
        }

        public static void AdbRemovePortReverse(int port, bool ownsPortForward, ILogger logger)
        {
            if (ownsPortForward)
            {
                if (!RunAdbCommandInternal($"reverse --remove tcp:{port}", "", 0, logger))
                {
                    logger?.LogError($"Failed removing port forward for tcp:{port}.");
                }
            }
        }

        public static bool RunAdbCommandInternal(string command, string expectedOutput, int expectedExitCode, ILogger logger)
        {
            string sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            string adbTool = "adb";

            if (!string.IsNullOrEmpty(sdkRoot))
            {
                adbTool = sdkRoot + Path.DirectorySeparatorChar + "platform-tools" + Path.DirectorySeparatorChar + adbTool;
            }

            logger?.LogDebug($"Executing {adbTool} {command}.");

            Process process = new();
            process.StartInfo.FileName = adbTool;
            process.StartInfo.Arguments = command;

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardInput = false;

            bool processStartedResult = false;
            bool expectedOutputResult = true;
            bool expectedExitCodeResult = true;

            try
            {
                processStartedResult = process.Start();
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed executing {adbTool} {command}. Error: {ex.Message}.");
            }

            if (processStartedResult)
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                if (!string.IsNullOrEmpty(expectedOutput))
                {
                    expectedOutputResult = !string.IsNullOrEmpty(stdout) ? stdout.Contains(expectedOutput) : false;
                }

                if (!string.IsNullOrEmpty(stdout))
                {
                    logger.LogTrace($"stdout: {stdout.TrimEnd()}");
                }

                if (!string.IsNullOrEmpty(stderr))
                {
                    logger.LogError($"stderr: {stderr.TrimEnd()}");
                }
            }

            if (processStartedResult)
            {
                process.WaitForExit();
                expectedExitCodeResult = (expectedExitCode != -1) ? (process.ExitCode == expectedExitCode) : true;
            }

            return processStartedResult && expectedOutputResult && expectedExitCodeResult;
        }
    }

    internal sealed class ADBTcpServerRouterFactory : TcpServerRouterFactory
    {
        private readonly int _port;
        private bool _ownsPortReverse;
        private Task _portReverseTask;
        private CancellationTokenSource _portReverseTaskCancelToken;

        public static TcpServerRouterFactory CreateADBInstance(string tcpServer, int runtimeTimeoutMs, ILogger logger)
        {
            return new ADBTcpServerRouterFactory(tcpServer, runtimeTimeoutMs, logger);
        }

        public ADBTcpServerRouterFactory(string tcpServer, int runtimeTimeoutMs, ILogger logger)
            : base(tcpServer, runtimeTimeoutMs, logger)
        {
            _port = new IpcTcpSocketEndPoint(tcpServer).EndPoint.Port;
        }

        public override void Start()
        {
            // Enable port reverse.
            _ownsPortReverse = ADBCommandExec.AdbAddPortReverse(_port, Logger);

            _portReverseTaskCancelToken = new CancellationTokenSource();
            _portReverseTask = Task.Run(() => {
                while (!_portReverseTaskCancelToken.Token.IsCancellationRequested)
                {
                    try
                    {
                        Task.Delay(5000, _portReverseTaskCancelToken.Token).Wait();
                    }
                    catch { }

                    if (!_portReverseTaskCancelToken.Token.IsCancellationRequested)
                    {
                        // Make sure reverse port configuration for port is still active.
                        if (ADBCommandExec.AdbAddPortReverse(_port, Logger) && !_ownsPortReverse)
                        {
                            _ownsPortReverse = true;
                        }
                    }
                }
            }, _portReverseTaskCancelToken.Token);

            base.Start();
        }

        public override async Task Stop()
        {
            await base.Stop().ConfigureAwait(false);

            try
            {
                _portReverseTaskCancelToken.Cancel();
                await _portReverseTask.ConfigureAwait(false);
            }
            catch { }

            // Disable port reverse.
            ADBCommandExec.AdbRemovePortReverse(_port, _ownsPortReverse, Logger);
            _ownsPortReverse = false;
        }
    }

    internal sealed class ADBTcpClientRouterFactory : TcpClientRouterFactory
    {
        private readonly int _port;
        private bool _ownsPortForward;
        private Task _portForwardTask;
        private CancellationTokenSource _portForwardTaskCancelToken;

        public static TcpClientRouterFactory CreateADBInstance(string tcpClient, int runtimeTimeoutMs, ILogger logger)
        {
            return new ADBTcpClientRouterFactory(tcpClient, runtimeTimeoutMs, logger);
        }

        public ADBTcpClientRouterFactory(string tcpClient, int runtimeTimeoutMs, ILogger logger)
            : base(tcpClient, runtimeTimeoutMs, logger)
        {
            _port = new IpcTcpSocketEndPoint(tcpClient).EndPoint.Port;
        }

        public override void Start()
        {
            // Enable port forwarding.
            _ownsPortForward = ADBCommandExec.AdbAddPortForward(_port, _logger);

            _portForwardTaskCancelToken = new CancellationTokenSource();
            _portForwardTask = Task.Run(() => {
                while (!_portForwardTaskCancelToken.Token.IsCancellationRequested)
                {
                    try
                    {
                        Task.Delay(5000, _portForwardTaskCancelToken.Token).Wait();
                    }
                    catch { }

                    if (!_portForwardTaskCancelToken.Token.IsCancellationRequested)
                    {
                        // Make sure forward port configuration for port is still active.
                        if (ADBCommandExec.AdbAddPortForward(_port, _logger) && !_ownsPortForward)
                        {
                            _ownsPortForward = true;
                        }
                    }
                }
            }, _portForwardTaskCancelToken.Token);
        }

        public override void Stop()
        {
            try
            {
                _portForwardTaskCancelToken.Cancel();
                _portForwardTask.Wait();
            }
            catch { }

            // Disable port forwarding.
            ADBCommandExec.AdbRemovePortForward(_port, _ownsPortForward, _logger);
            _ownsPortForward = false;
        }
    }
}
