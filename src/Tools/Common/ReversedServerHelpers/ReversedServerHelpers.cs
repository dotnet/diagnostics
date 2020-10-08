// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Internal.Common.Utils
{
    // <summary>
    // ProcessLauncher is a child-process launcher for "diagnostics tools at startup" scenarios
    // It launches the target process at startup and passes its processId to the corresponding Command handler.
    // </summary>
    internal class ProcessLauncher
    {
        private Process _childProc;
        public ManualResetEvent HasExited;

        internal static ProcessLauncher Launcher = new ProcessLauncher();

        public void PrepareChildProcess(List<string> args)
        {
            _childProc = new Process();
            _childProc.StartInfo.FileName = args[0];
            _childProc.StartInfo.Arguments = String.Join(" ", args.GetRange(1, args.Count - 1));
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
            HasExited = new ManualResetEvent(false);
            _childProc.StartInfo.UseShellExecute = false;
            _childProc.StartInfo.RedirectStandardOutput = true;
            _childProc.StartInfo.RedirectStandardError = true;
            _childProc.StartInfo.RedirectStandardInput = true;
            _childProc.StartInfo.Environment.Add("DOTNET_DiagnosticPorts", $"{diagnosticTransportName},suspend");
            _childProc.Exited += new EventHandler(OnExited);
            try
            {
                _childProc.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Cannot start target process: {_childProc.StartInfo.FileName}");
                Console.WriteLine(e.ToString());
                return false;
            }
            return true;
        }

        private static void OnExited(object sender, EventArgs args)
        {
            ProcessLauncher.Launcher.HasExited.Set();
        }
    }

    // <summary>
    // This class acts a helper class for building a DiagnosticsClient instance
    // </summary>
    internal class ReversedDiagnosticsClientBuilder
    {
        private static string GetRandomTransportName() => "DOTNET_TOOL_PATH" + Path.GetRandomFileName();
        private string diagnosticTransportName;
        private ReversedDiagnosticsServer server;
        private ProcessLauncher _childProcLauncher;

        public ReversedDiagnosticsClientBuilder(ProcessLauncher childProcLauncher)
        {
            diagnosticTransportName = GetRandomTransportName();
            _childProcLauncher = childProcLauncher;
            server = new ReversedDiagnosticsServer(diagnosticTransportName);
            server.Start();
        }

        // <summary>
        // Starts the child process and returns the diagnostics client once the child proc connects to the reversed diagnostics pipe.
        // The callee needs to resume the diagnostics client at appropriate time.
        // </summary>
        public DiagnosticsClient Build(int timeoutInSec)
        {
            if (!_childProcLauncher.HasChildProc)
            {
                throw new InvalidOperationException("Must have a valid child process to launch.");
            }
            if (!_childProcLauncher.Start(diagnosticTransportName))
            {
                throw new InvalidOperationException("Failed to start dotnet-counters.");
            }
            IpcEndpointInfo endpointInfo = server.Accept(TimeSpan.FromSeconds(timeoutInSec));
            return new DiagnosticsClient(endpointInfo.Endpoint);
        }
    }
}