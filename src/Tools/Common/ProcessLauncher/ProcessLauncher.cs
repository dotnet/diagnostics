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

        public bool Start(bool isInteractive, string diagnosticTransportName)
        {
            if (!isInteractive)
            {
                _childProc.StartInfo.UseShellExecute = false;
                _childProc.StartInfo.RedirectStandardOutput = true;
                _childProc.StartInfo.RedirectStandardError = true;
            }
            else
            {
                // TODO FIXME: Might not be necessary here.
                _childProc.StartInfo.UseShellExecute = false;
                _childProc.StartInfo.RedirectStandardOutput = false;
                _childProc.StartInfo.RedirectStandardError = false;
            }
            _childProc.StartInfo.Environment.Add("DOTNET_DiagnosticPorts", $"{diagnosticTransportName},suspend");
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

        public void KillChildProc()
        {
            _childProc.Kill();
        }
    }

    internal class ReversedDiagnosticsClientBuilder
    {
        private static string GetRandomTransportName() => "DOTNET_TOOL_PATH" + Path.GetRandomFileName();
        private string diagnosticTransportName;
        public ReversedDiagnosticsClientBuilder()
        {
            diagnosticTransportName = GetRandomTransportName();
            ReversedDiagnosticsServer server = new ReversedDiagnosticsServer(diagnosticTransportName);
            server.Start();
        }

        public async Task<DiagnosticsClient> Build(bool isInteractive)
        {
            if (!ProcessLauncher.Launcher.HasChildProc)
            {
                throw new InvalidOperationException("Must have a valid child process to launch.");
            }
            ProcessLauncher.Launcher.Start(isInteractive, diagnosticTransportName);
            ReversedDiagnosticsServer server = new ReversedDiagnosticsServer(diagnosticTransportName);
            IpcEndpointInfo endpointInfo = await server.AcceptAsync(new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token);
            return new DiagnosticsClient(endpointInfo.Endpoint);
        }
    }
}