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
        private Process _childProc = null;

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
            _childProc.StartInfo.UseShellExecute = false;
            _childProc.StartInfo.RedirectStandardOutput = true;
            _childProc.StartInfo.RedirectStandardError = true;
            _childProc.StartInfo.RedirectStandardInput = true;
            _childProc.StartInfo.Environment.Add("DOTNET_DiagnosticPorts", $"{diagnosticTransportName},suspend");
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

    // <summary>
    // This class acts a helper class for building a DiagnosticsClient instance
    // </summary>
    internal class ReversedDiagnosticsClientBuilder
    {
        private static string GetTransportName(string toolName) => $"{toolName}-{Process.GetCurrentProcess().Id}-{DateTime.Now:yyyyMMdd_HHmmss}.socket";

        // <summary>
        // Starts the child process and returns the diagnostics client once the child proc connects to the reversed diagnostics pipe.
        // The callee needs to resume the diagnostics client at appropriate time.
        // </summary>
        public static DiagnosticsClient Build(ProcessLauncher childProcLauncher, string toolName, int timeoutInSec)
        {
            if (!childProcLauncher.HasChildProc)
            {
                throw new InvalidOperationException("Must have a valid child process to launch.");
            }
            // Create and start the reversed server            
            string diagnosticTransportName = GetTransportName(toolName);
            ReversedDiagnosticsServer server = new ReversedDiagnosticsServer(diagnosticTransportName);
            server.Start();

            // Start the child proc
            if (!childProcLauncher.Start(diagnosticTransportName))
            {
                throw new InvalidOperationException("Failed to start dotnet-counters.");
            }

            // Wait for attach
            IpcEndpointInfo endpointInfo = server.Accept(TimeSpan.FromSeconds(timeoutInSec));

            // If for some reason a different process attached to us, wait until the expected process attaches.
            while (endpointInfo.ProcessId != childProcLauncher.ChildProc.Id)
            {
               endpointInfo = server.Accept(TimeSpan.FromSeconds(timeoutInSec)); 
            }
            return new DiagnosticsClient(endpointInfo.Endpoint);
        }
    }
}