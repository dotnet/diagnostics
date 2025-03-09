// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Internal.Common.Utils
{
    internal sealed partial class DsRouterProcessLauncher
    {
        private Process _childProc;
        private Task _stdOutTask = Task.CompletedTask;
        private Task _stdErrTask = Task.CompletedTask;
        internal static DsRouterProcessLauncher Launcher = new();
        private bool _processStarted;

        private static async Task ReadAndIgnoreAllStreamAsync(StreamReader streamToIgnore, CancellationToken cancelToken)
        {
            Memory<char> memory = new char[4096];
            while (await streamToIgnore.ReadAsync(memory, cancelToken).ConfigureAwait(false) != 0)
            {
            }
        }

        private bool HasChildProc => _childProc != null;

        private Process ChildProc => _childProc;

        public int Start(string dsroutercommand, CancellationToken ct)
        {
            string toolsRoot = System.IO.Path.GetDirectoryName(System.Environment.ProcessPath);
            string dotnetDsrouterTool = "dotnet-dsrouter";

            if (!string.IsNullOrEmpty(toolsRoot))
            {
                dotnetDsrouterTool = Path.Combine(toolsRoot, dotnetDsrouterTool);
            }

            _childProc = new Process();

            _childProc.StartInfo.FileName = dotnetDsrouterTool;
            _childProc.StartInfo.Arguments = dsroutercommand;
            _childProc.StartInfo.UseShellExecute = false;
            _childProc.StartInfo.RedirectStandardOutput = true;
            _childProc.StartInfo.RedirectStandardError = true;
            _childProc.StartInfo.RedirectStandardInput = true;
            try
            {
                _childProc.Start();
                _processStarted = true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"An error occurred trying to start process '{_childProc.StartInfo.FileName}' with working directory '{System.IO.Directory.GetCurrentDirectory()}'");
                Console.WriteLine($"{e.Message}");
                return -1;
            }

            _stdErrTask = ReadAndIgnoreAllStreamAsync(_childProc.StandardError, ct);
            _stdOutTask = ReadAndIgnoreAllStreamAsync(_childProc.StandardOutput, ct);
            Task.Delay(1000, ct).Wait(ct);
            return !_childProc.HasExited ? _childProc.Id : -2;
        }

        public void Cleanup()
        {
            if (_childProc != null && _processStarted && !_childProc.HasExited)
            {
                try
                {
                    _childProc.Kill();
                }
                // if process exited while we were trying to kill it, it can throw IOE
                catch (InvalidOperationException) { }
                _stdOutTask.Wait();
                _stdErrTask.Wait();
            }
        }
    }
}
