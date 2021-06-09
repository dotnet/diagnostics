using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Tools.DiagnosticsServerRouter
{
    internal class ADBTcpClientRouterFactory : TcpClientRouterFactory
    {
        readonly int _port;
        bool ownsPortForward;

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
            AdbAddPortForward();
        }

        public override void Stop()
        {
            // Disable port forwarding.
            AdbRemovePortForward();
        }

        void AdbAddPortForward()
        {
            if (!RunAdbCommandInternal($"forward --list", $"tcp:{_port}", 0))
            {
                ownsPortForward = RunAdbCommandInternal($"forward tcp:{_port} tcp:{_port}", "", 0);
                if (!ownsPortForward)
                    _logger?.LogError($"Failed creating port forward for tcp:{_port}.");
            }
        }

        void AdbRemovePortForward()
        {
            if (ownsPortForward)
            {
                if (!RunAdbCommandInternal($"forward --remove tcp:{_port}", "", 0))
                    _logger?.LogError($"Failed removing port forward for tcp:{_port}.");
            }
            ownsPortForward = false;
        }

        bool RunAdbCommandInternal(string command, string expectedOutput, int expectedExitCode)
        {
            var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            var adbTool = "adb";

            if (!string.IsNullOrEmpty(sdkRoot))
                adbTool = sdkRoot + Path.DirectorySeparatorChar + "platform-tools" + Path.DirectorySeparatorChar + adbTool;

            _logger?.LogDebug($"Executing {adbTool} {command}.");

            var process = new Process();
            process.StartInfo.FileName = adbTool;
            process.StartInfo.Arguments = command;

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = !string.IsNullOrEmpty(expectedOutput);
            process.StartInfo.RedirectStandardError = false;
            process.StartInfo.RedirectStandardInput = false;

            bool result = false;
            try
            {
                result = process.Start();
            }
            catch (Exception)
            {
            }

            if (result && !string.IsNullOrEmpty(expectedOutput))
            {
                var stdout = process.StandardOutput.ReadToEnd();
                result = stdout.Contains(expectedOutput);
            }

            process.WaitForExit();

            if (result && expectedExitCode != -1)
            {
                result = process.ExitCode == expectedExitCode;
            }

            return result;
        }
    }
}
