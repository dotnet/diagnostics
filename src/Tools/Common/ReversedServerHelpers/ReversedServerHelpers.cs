// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Microsoft.Internal.Common.Utils
{
    // <summary>
    // ProcessLauncher is a child-process launcher for "diagnostics tools at startup" scenarios
    // It launches the target process at startup and passes its processId to the corresponding Command handler.
    // </summary>
    internal class ProcessLauncher
    {
        private Process _childProc = null;

        internal static ProcessLauncher Launcher = new ProcessLauncher();

        public void PrepareChildProcess(string[] args)
        {
            int unparsedTokenIdx = FindUnparsedTokenIndex(args);
            if (unparsedTokenIdx < 0)
            {
                return;
            }

            _childProc = new Process();
            _childProc.StartInfo.FileName = args[unparsedTokenIdx];
            string arguments = "";
            for (int i = unparsedTokenIdx+1; i < args.Length; i++)
            {
                if (args[i].Contains(" "))
                {
                    arguments += $"\"{args[i].Replace("\"", "\\\"")}\"";
                }
                else
                {
                    arguments += args[i];
                }

                if (i != args.Length)
                    arguments += " ";
            }
            _childProc.StartInfo.Arguments = arguments;
        }

        private int FindUnparsedTokenIndex(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--" && i < (args.Length - 1)) return i+1;
            }
            return -1;
        }

        public bool HasChildProc
        {
            get
            {
                return _childProc != null;
            }
        }

        public Process ChildProc
        {
            get
            {
                return _childProc;
            }
        }
        public bool Start(string diagnosticTransportName)
        {
            _childProc.StartInfo.UseShellExecute = false;
            _childProc.StartInfo.RedirectStandardOutput = true;
            _childProc.StartInfo.RedirectStandardError = true;
            _childProc.StartInfo.RedirectStandardInput = true;
            _childProc.StartInfo.Environment.Add("DOTNET_DiagnosticPorts", $"{diagnosticTransportName}");
            try
            {
                _childProc.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Cannot start target process: {_childProc.StartInfo.FileName} {_childProc.StartInfo.Arguments}");
                Console.WriteLine(e.ToString());
                return false;
            }
            return true;
        }

        public void Cleanup()
        {
            if (_childProc != null && !_childProc.HasExited)
            {
                try
                {
                    _childProc.Kill();
                }
                // if process exited while we were trying to kill it, it can throw IOE 
                catch (InvalidOperationException) { }
            }
        }
    }

    internal class DiagnosticsClientHolder : IDisposable
    {
        public DiagnosticsClient Client;
        public IpcEndpointInfo EndpointInfo;

        private readonly string _port;

        public DiagnosticsClientHolder(DiagnosticsClient client)
        {
            Client = client;
            _port = null;
        }

        public DiagnosticsClientHolder(DiagnosticsClient client, IpcEndpointInfo endpointInfo)
        {
            Client = client;
            EndpointInfo = endpointInfo;
            _port = null;
        }

        public DiagnosticsClientHolder(DiagnosticsClient client, IpcEndpointInfo endpointInfo, string port)
        {
            Client = client;
            EndpointInfo = endpointInfo;
            _port = port;
        }

        public void Dispose()
        {
            if (!string.IsNullOrEmpty(_port) && File.Exists(_port))
            {
                File.Delete(_port);
            }
            ProcessLauncher.Launcher.Cleanup();
        }
    }

    // <summary>
    // This class acts a helper class for building a DiagnosticsClient instance
    // </summary>
    internal class DiagnosticsClientBuilder
    {
        private string _toolName;
        private int _timeoutInSec;

        private string GetTransportName(string toolName) => $"{toolName}-{Process.GetCurrentProcess().Id}-{DateTime.Now:yyyyMMdd_HHmmss}.socket";

        public DiagnosticsClientBuilder(string toolName, int timeoutInSec)
        {
            _toolName = toolName;
            _timeoutInSec = timeoutInSec;
        }

        public async Task<DiagnosticsClientHolder> Build(CancellationToken ct, int processId, string portName)
        {
            if (ProcessLauncher.Launcher.HasChildProc)
            {
                // Create and start the reversed server            
                string diagnosticTransportName = GetTransportName(_toolName);
                ReversedDiagnosticsServer server = new ReversedDiagnosticsServer(diagnosticTransportName);
                server.Start();

                // Start the child proc
                if (!ProcessLauncher.Launcher.Start(diagnosticTransportName))
                {
                    throw new InvalidOperationException($"Failed to start {ProcessLauncher.Launcher.ChildProc.ProcessName}.");
                }
                IpcEndpointInfo endpointInfo;
                try
                {
                    // Wait for attach
                    endpointInfo = server.Accept(TimeSpan.FromSeconds(_timeoutInSec));

                    // If for some reason a different process attached to us, wait until the expected process attaches.
                    while (endpointInfo.ProcessId != ProcessLauncher.Launcher.ChildProc.Id)
                    {
                        endpointInfo = server.Accept(TimeSpan.FromSeconds(_timeoutInSec));
                    }
                }
                catch (TimeoutException)
                {
                    Console.Error.WriteLine("Unable to start tracing session - the target app failed to connect to the diagnostics port. This may happen if the target application is running .NET Core 3.1 or older versions. Attaching at startup is only available from .NET 5.0 or later.");
                    throw;
                }
                return new DiagnosticsClientHolder(new DiagnosticsClient(endpointInfo.Endpoint), endpointInfo);
            }
            else if (!string.IsNullOrEmpty(portName))
            {
                ReversedDiagnosticsServer server = new ReversedDiagnosticsServer(portName);
                server.Start();
                string fullPort = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? portName : Path.GetFullPath(portName);
                Console.WriteLine($"Waiting for connection on {fullPort}");
                Console.WriteLine($"Start an application with the following environment variable: DOTNET_DiagnosticPorts={fullPort}");

                IpcEndpointInfo endpointInfo = await server.AcceptAsync(ct);
                return new DiagnosticsClientHolder(new DiagnosticsClient(endpointInfo.Endpoint), endpointInfo, fullPort);
            }
            else
            {
                return new DiagnosticsClientHolder(new DiagnosticsClient(processId));
            }
        }
    }
}