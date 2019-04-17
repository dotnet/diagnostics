// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CollectCommandHandler
    {
        /// <summary>
        /// Collects a diagnostic trace from a currently running process.
        /// </summary>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the trace from.</param>
        /// <param name="output">The output path for the collected trace data.</param>
        /// <param name="buffersize">Sets the size of the in-memory circular buffer in megabytes.</param>
        /// <param name="providers">A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: '(GUID|KnownProviderName)[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'</param>
        /// <returns></returns>
        public static async Task<int> Collect(IConsole console, int processId, string output, uint buffersize, string providers)
        {
            try
            {
                if (output == null)
                    throw new ArgumentNullException(nameof(output));

                var configuration = new SessionConfiguration(
                    circularBufferSizeMB: buffersize,
                    outputPath: null, // Not used on the streaming scenario.
                    Extensions.ToProviders(providers));

                var shouldExit = new ManualResetEvent(false);

                ulong sessionId = 0;
                using (Stream stream = EventPipeClient.CollectTracing(processId, configuration, out sessionId))
                using (VirtualTerminalMode vTermMode = VirtualTerminalMode.TryEnable())
                {
                    if (sessionId == 0)
                    {
                        Console.Error.WriteLine("Unable to create session.");
                        return -1;
                    }

                    var collectingTask = new Task(() => {
                        using (var fs = new FileStream(output, FileMode.Create, FileAccess.Write))
                        {
                            Console.Out.WriteLine($"Recording tracing session to: {fs.Name}");
                            Console.Out.WriteLine($"\tSession Id: 0x{sessionId:X16}");

                            while (true)
                            {
                                var buffer = new byte[16 * 1024];
                                int nBytesRead = stream.Read(buffer, 0, buffer.Length);
                                if (nBytesRead <= 0)
                                    break;
                                fs.Write(buffer, 0, nBytesRead);

                                ResetCurrentConsoleLine(vTermMode.IsEnabled);
                                Console.Out.Write($"\tRecording trace {GetSize(fs.Length)}");

                                Debug.WriteLine($"PACKET: {Convert.ToBase64String(buffer, 0, nBytesRead)} (bytes {nBytesRead})");
                            }
                        }
                    });
                    collectingTask.Start();

                    Console.Out.WriteLine("press <Enter> or <Ctrl+c> to exit...");
                    System.Console.CancelKeyPress += (sender, args) => {
                        args.Cancel = true;
                        shouldExit.Set();
                    };

                    do {
                        while (!Console.KeyAvailable && !shouldExit.WaitOne(250)) { }
                    } while (!shouldExit.WaitOne(0) && Console.ReadKey(true).Key != ConsoleKey.Enter);

                    EventPipeClient.StopTracing(processId, sessionId);
                    collectingTask.Wait();
                }

                Console.Out.WriteLine();
                Console.Out.WriteLine("Trace completed.");

                await Task.FromResult(0);
                return sessionId != 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return 1;
            }
        }

        private static void ResetCurrentConsoleLine(bool isVTerm)
        {
            if (isVTerm)
            {
                Console.Out.Write("\u001b[2K\u001b[1000D");
            }
            else
            {
                Console.Out.Write("\r" + new string(' ', Console.BufferWidth - 1) + "\r");
            }
        }

        private static string GetSize(long length)
        {
            if (length > 1e9)
                return $"{length / 1e9:0.00##} (GB)";
            else if (length > 1e6)
                return $"{length / 1e6:0.00##} (MB)";
            else if (length > 1e3)
                return $"{length / 1e3:0.00##} (KB)";
            else
                return $"{length / 1.0:0.00##} (byte)";
        }

        public static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Collects a diagnostic trace from a currently running process",
                symbols: new Option[] {
                    CommonOptions.ProcessIdOption(),
                    CommonOptions.CircularBufferOption(),
                    CommonOptions.OutputPathOption(),
                    CommonOptions.ProvidersOption(),
                },
                handler: CommandHandler.Create<IConsole, int, string, uint, string>(Collect));
    }
}
