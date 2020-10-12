// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

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