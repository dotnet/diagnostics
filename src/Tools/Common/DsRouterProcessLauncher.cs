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

        private static async Task ReadAndLogAllLinesAsync(StreamReader streamToRead, TextWriter output, CancellationToken cancelToken)
        {
            string line;
            while ((line = await streamToRead.ReadLineAsync(cancelToken).ConfigureAwait(false)) != null)
            {
                // Just log with no colors if redirected
                if (Console.IsOutputRedirected)
                {
                    output.WriteLine(line);
                    continue;
                }

                // Console coloring is not preserved, so this is a naive approach based on SimpleConsoleFormatter's output:
                // https://github.com/dotnet/runtime/blob/aadcceeb03ce0ecbc2ad645de0feb10189daa64c/src/libraries/Microsoft.Extensions.Logging.Console/src/SimpleConsoleFormatter.cs#L163-L199

                ConsoleColor foregroundColor = Console.ForegroundColor;
                ConsoleColor backgroundColor = Console.BackgroundColor;
                try
                {
                    // Specific dotnet-dsrouter warning message
                    if (line.StartsWith("WARNING: dotnet-dsrouter", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        output.WriteLine(line);
                        continue;
                    }

                    // SimpleConsoleFormatter prefixes
                    if (line.StartsWith("crit:", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.BackgroundColor = ConsoleColor.DarkRed;
                        output.Write("crit");
                    }
                    else if (line.StartsWith("fail:", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.BackgroundColor = ConsoleColor.DarkRed;
                        output.Write("fail");
                    }
                    else if (line.StartsWith("warn:", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.BackgroundColor = ConsoleColor.Black;
                        output.Write("warn");
                    }
                    else if (line.StartsWith("info:", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.BackgroundColor = ConsoleColor.Black;
                        output.Write("info");
                    }
                    else if (line.StartsWith("dbug:", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.BackgroundColor = ConsoleColor.Black;
                        output.Write("dbug");
                    }
                    else if (line.StartsWith("trce:", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.BackgroundColor = ConsoleColor.Black;
                        output.Write("trce");
                    }
                    else
                    {
                        output.WriteLine(line);
                        continue; // If it doesn't match any prefix, just write the line as is
                    }
                }
                finally
                {
                    Console.ForegroundColor = foregroundColor;
                    Console.BackgroundColor = backgroundColor;
                }

                // If we get here, we logged a prefix, so we can log the rest of the line
                if (line.Length > 4)
                {
                    output.WriteLine(line.AsSpan().Slice(4));
                }
                else
                {
                    // If the line is just the prefix, we still need to write a new line
                    output.WriteLine();
                }
            }
        }

        private bool HasChildProc => _childProc != null;

        private Process ChildProc => _childProc;

        public int Start(string dsrouterCommand, CancellationToken ct)
        {
            string toolsRoot = System.IO.Path.GetDirectoryName(System.Environment.ProcessPath);
            string dotnetDsrouterTool = "dotnet-dsrouter";

            if (!string.IsNullOrEmpty(toolsRoot))
            {
                dotnetDsrouterTool = Path.Combine(toolsRoot, dotnetDsrouterTool);
            }

            // Block SIGINT and SIGQUIT in child process.
            dsrouterCommand += " --block-signals SIGINT;SIGQUIT";

            _childProc = new Process();

            _childProc.StartInfo.FileName = dotnetDsrouterTool;
            _childProc.StartInfo.Arguments = dsrouterCommand;
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

            _stdErrTask = ReadAndLogAllLinesAsync(_childProc.StandardError, Console.Error, ct);
            _stdOutTask = ReadAndLogAllLinesAsync(_childProc.StandardOutput, Console.Out, ct);
            Task.Delay(1000, ct).Wait(ct);
            return !_childProc.HasExited ? _childProc.Id : -2;
        }

        public void Cleanup()
        {
            if (_childProc != null && _processStarted && !_childProc.HasExited)
            {
                try
                {
                    _childProc.StandardInput.WriteLine("Q");
                    _childProc.StandardInput.Flush();

                    _childProc.WaitForExit(1000);
                }
                // if process exited while we were trying to write to stdin, it can throw IOException
                catch (IOException) { }

                try
                {
                    if (!_childProc.HasExited)
                    {
                        _childProc.Kill();
                    }
                }
                // if process exited while we were trying to kill it, it can throw IOE
                catch (InvalidOperationException) { }

                try
                {
                    _stdOutTask.Wait();
                }
                // Ignore any fault/cancel state of task.
                catch (AggregateException) { }

                try
                {
                    _stdErrTask.Wait();
                }
                // Ignore any fault/cancel state of task.
                catch (AggregateException) { }
            }
        }
    }
}
