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
        public static bool AdbAddPortForward(int localPort, int remotePort, bool rethrow, ILogger logger)
        {
            bool ownsPortForward = false;
            if (!RunAdbCommandInternal($"forward --list", $"tcp:{localPort}", 0, rethrow, logger))
            {
                ownsPortForward = RunAdbCommandInternal($"forward tcp:{localPort} tcp:{remotePort}", "", 0, rethrow, logger);
                if (!ownsPortForward)
                {
                    logger?.LogError($"Failed setting up port forward for host tcp:{localPort} <-> device tcp:{remotePort}.");
                }
            }
            return ownsPortForward;
        }

        public static bool AdbAddPortReverse(int localPort, int remotePort, bool rethrow, ILogger logger)
        {
            bool ownsPortForward = false;
            if (!RunAdbCommandInternal($"reverse --list", $"tcp:{remotePort}", 0, rethrow, logger))
            {
                ownsPortForward = RunAdbCommandInternal($"reverse tcp:{remotePort} tcp:{localPort}", "", 0, rethrow, logger);
                if (!ownsPortForward)
                {
                    logger?.LogError($"Failed setting up port forward for host tcp:{localPort} <-> device tcp:{remotePort}.");
                }
            }
            return ownsPortForward;
        }

        public static void AdbRemovePortForward(int localPort, int remotePort, bool ownsPortForward, bool rethrow, ILogger logger)
        {
            if (ownsPortForward)
            {
                if (!RunAdbCommandInternal($"forward --remove tcp:{localPort}", "", 0, rethrow, logger))
                {
                    logger?.LogError($"Failed setting up port forward for host tcp:{localPort} <-> device tcp:{remotePort}.");
                }
            }
        }

        public static void AdbRemovePortReverse(int localPort, int remotePort, bool ownsPortForward, bool rethrow, ILogger logger)
        {
            if (ownsPortForward)
            {
                if (!RunAdbCommandInternal($"reverse --remove tcp:{remotePort}", "", 0, rethrow, logger))
                {
                    logger?.LogError($"Failed setting up port forward for host tcp:{localPort} <-> device tcp:{remotePort}.");
                }
            }
        }

        public static bool RunAdbCommandInternal(string command, string expectedOutput, int expectedExitCode, bool rethrow, ILogger logger)
        {
            string sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            string adbTool = "adb";

            if (!string.IsNullOrEmpty(sdkRoot))
            {
                adbTool = Path.Combine(sdkRoot, "platform-tools", adbTool);
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
                logger.LogError($"Failed executing {adbTool} {command}. Error: {ex.Message}");
                if (rethrow)
                {
                    throw ex;
                }
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

                process.WaitForExit();
                expectedExitCodeResult = (expectedExitCode != -1) ? (process.ExitCode == expectedExitCode) : true;
            }

            return processStartedResult && expectedOutputResult && expectedExitCodeResult;
        }
    }

    internal sealed class ADBTcpServerRouterFactory : TcpServerRouterFactory
    {
        private readonly int _localPort;
        private readonly int _remotePort;
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
            _localPort = new IpcTcpSocketEndPoint(tcpServer).EndPoint.Port;
            _remotePort = _localPort - 1;

            if (_remotePort <= 0)
            {
                throw new ArgumentException($"Invalid local/remote TCP endpoint ports {_localPort}/{_remotePort}.");
            }
        }

        public override void Start()
        {
            // Enable port reverse.
            try
            {
                _ownsPortReverse = ADBCommandExec.AdbAddPortReverse(_localPort, _remotePort, true, Logger);
            }
            catch
            {
                _ownsPortReverse = false;
                Logger.LogError("Failed setting up adb port reverse." +
                    " This might lead to problems communicating with Android application." +
                    " Make sure env variable ANDROID_SDK_ROOT is set and points to an Android SDK." +
                    $" Executing with unknown adb status for port {_localPort}.");
                base.Start();
                return;
            }

            _portReverseTaskCancelToken = new CancellationTokenSource();
            _portReverseTask = Task.Run(async () => {
                using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
                while (await timer.WaitForNextTickAsync(_portReverseTaskCancelToken.Token).ConfigureAwait(false) && !_portReverseTaskCancelToken.Token.IsCancellationRequested)
                {
                    // Make sure reverse port configuration is still active.
                    if (ADBCommandExec.AdbAddPortReverse(_localPort, _remotePort, false, Logger) && !_ownsPortReverse)
                    {
                        _ownsPortReverse = true;
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
            ADBCommandExec.AdbRemovePortReverse(_localPort, _remotePort, _ownsPortReverse, false, Logger);
            _ownsPortReverse = false;
        }
    }

    internal sealed class ADBTcpClientRouterFactory : TcpClientRouterFactory
    {
        private readonly int _localPort;
        private readonly int _remotePort;
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
            _localPort = new IpcTcpSocketEndPoint(tcpClient).EndPoint.Port;
            _remotePort = _localPort - 1;

            if (_remotePort <= 0)
            {
                throw new ArgumentException($"Invalid local/remote TCP endpoint ports {_localPort}/{_remotePort}.");
            }
        }

        public override void Start()
        {
            // Enable port forwarding.
            try
            {
                _ownsPortForward = ADBCommandExec.AdbAddPortForward(_localPort, _remotePort, true, Logger);
            }
            catch
            {
                _ownsPortForward = false;
                Logger.LogError("Failed setting up adb port forward." +
                    " This might lead to problems communicating with Android application." +
                    " Make sure env variable ANDROID_SDK_ROOT is set and points to an Android SDK." +
                    $" Executing with unknown adb status for port {_localPort}.");
                return;
            }

            _portForwardTaskCancelToken = new CancellationTokenSource();
            _portForwardTask = Task.Run(async () => {
                using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
                while (await timer.WaitForNextTickAsync(_portForwardTaskCancelToken.Token).ConfigureAwait(false) && !_portForwardTaskCancelToken.Token.IsCancellationRequested)
                {
                    // Make sure forward port configuration is still active.
                    if (ADBCommandExec.AdbAddPortForward(_localPort, _remotePort, false, Logger) && !_ownsPortForward)
                    {
                        _ownsPortForward = true;
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
            ADBCommandExec.AdbRemovePortForward(_localPort, _remotePort, _ownsPortForward, false, Logger);
            _ownsPortForward = false;
        }
    }
}
