// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
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

            var process = new Process();
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
            catch (Exception)
            {
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
                    logger.LogTrace($"stdout: {stdout}");
                }

                if (!string.IsNullOrEmpty(stderr))
                {
                    logger.LogError($"stderr: {stderr}");
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

    internal class ADBTcpServerRouterFactory : TcpServerRouterFactory
    {
        private readonly int _port;
        private bool _ownsPortReverse;

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
            _ownsPortReverse = ADBCommandExec.AdbAddPortReverse(_port, _logger);

            base.Start();
        }

        public override async Task Stop()
        {
            await base.Stop().ConfigureAwait(false);

            // Disable port reverse.
            ADBCommandExec.AdbRemovePortReverse(_port, _ownsPortReverse, _logger);
            _ownsPortReverse = false;
        }
    }

    internal class ADBTcpClientRouterFactory : TcpClientRouterFactory
    {
        private readonly int _port;
        private bool _ownsPortForward;

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
        }

        public override void Stop()
        {
            // Disable port forwarding.
            ADBCommandExec.AdbRemovePortForward(_port, _ownsPortForward, _logger);
            _ownsPortForward = false;
        }
    }
}
