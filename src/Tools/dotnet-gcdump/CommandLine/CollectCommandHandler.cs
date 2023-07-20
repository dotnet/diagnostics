// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Graphs;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class CollectCommandHandler
    {
        private delegate Task<int> CollectDelegate(CancellationToken ct, IConsole console, int processId, string output, int timeout, bool verbose, string name, string diagnosticPort);

        /// <summary>
        /// Collects a gcdump from a currently running process.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the gcdump from.</param>
        /// <param name="output">The output path for the collected gcdump.</param>
        /// <param name="timeout">The timeout for the collected gcdump.</param>
        /// <param name="verbose">Enable verbose logging.</param>
        /// <param name="name">The process name to collect the gcdump from.</param>
        /// <param name="diagnosticPort">The diagnostic IPC channel to collect the gcdump from.</param>
        /// <returns></returns>
        private static async Task<int> Collect(CancellationToken ct, IConsole console, int processId, string output, int timeout, bool verbose, string name, string diagnosticPort)
        {
            if (!CommandUtils.ValidateArgumentsForAttach (processId, name, diagnosticPort, out int resolvedProcessId))
            {
                return -1;
            }

            processId = resolvedProcessId;

            if (!string.IsNullOrEmpty(diagnosticPort))
            {
                try
                {
                    IpcEndpointConfig config = IpcEndpointConfig.Parse(diagnosticPort);
                    if (!config.IsConnectConfig)
                    {
                        Console.Error.WriteLine("--diagnostic-port is only supporting connect mode.");
                        return -1;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"--diagnostic-port argument error: {ex.Message}");
                    return -1;
                }

                processId = 0;
            }

            try
            {
                output = string.IsNullOrEmpty(output)
                    ? $"{DateTime.Now:yyyyMMdd\\_HHmmss}_{processId}.gcdump"
                    : output;

                FileInfo outputFileInfo = new(output);

                if (outputFileInfo.Exists)
                {
                    outputFileInfo.Delete();
                }

                if (string.IsNullOrEmpty(outputFileInfo.Extension) || outputFileInfo.Extension != ".gcdump")
                {
                    outputFileInfo = new FileInfo(outputFileInfo.FullName + ".gcdump");
                }

                Console.Out.WriteLine($"Writing gcdump to '{outputFileInfo.FullName}'...");

                Task<bool> dumpTask = Task.Run(() => {
                    if (TryCollectMemoryGraph(ct, processId, diagnosticPort, timeout, verbose, out MemoryGraph memoryGraph))
                    {
                        GCHeapDump.WriteMemoryGraph(memoryGraph, outputFileInfo.FullName, "dotnet-gcdump");
                        return true;
                    }

                    return false;
                });

                bool fDumpSuccess = await dumpTask.ConfigureAwait(false);

                if (fDumpSuccess)
                {
                    outputFileInfo.Refresh();
                    Console.Out.WriteLine($"\tFinished writing {outputFileInfo.Length} bytes.");
                    return 0;
                }
                else if (ct.IsCancellationRequested)
                {
                    Console.Out.WriteLine("\tCancelled.");
                    return -1;
                }
                else
                {
                    Console.Out.WriteLine("\tFailed to collect gcdump. Try running with '-v' for more information.");
                    return -1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex}");
                return -1;
            }
        }

        internal static bool TryCollectMemoryGraph(CancellationToken ct, int processId, string diagnosticPort, int timeout, bool verbose, out MemoryGraph memoryGraph)
        {
            DotNetHeapInfo heapInfo = new();
            TextWriter log = verbose ? Console.Out : TextWriter.Null;

            memoryGraph = new MemoryGraph(50_000);

            if (!EventPipeDotNetHeapDumper.DumpFromEventPipe(ct, processId, diagnosticPort, memoryGraph, log, timeout, heapInfo))
            {
                return false;
            }

            memoryGraph.AllowReading();
            return true;
        }

        public static Command CollectCommand() =>
            new(
                name: "collect",
                description: "Collects a diagnostic trace from a currently running process")
            {
                // Handler
                HandlerDescriptor.FromDelegate((CollectDelegate) Collect).GetCommandHandler(),
                // Options
                ProcessIdOption(),
                OutputPathOption(),
                VerboseOption(),
                TimeoutOption(),
                NameOption(),
                DiagnosticPortOption()
            };

        private static Option<int> ProcessIdOption() =>
            new(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect the gcdump from.")
            {
                Argument = new Argument<int>(name: "pid"),
            };

        private static Option<string> NameOption() =>
            new(
                aliases: new[] { "-n", "--name" },
                description: "The name of the process to collect the gcdump from.")
            {
                Argument = new Argument<string>(name: "name")
            };

        private static Option<string> OutputPathOption() =>
            new(
                aliases: new[] { "-o", "--output" },
                description: $@"The path where collected gcdumps should be written. Defaults to '.\YYYYMMDD_HHMMSS_<pid>.gcdump' where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path and file name of the dump.")
            {
                Argument = new Argument<string>(name: "gcdump-file-path", getDefaultValue: () => string.Empty)
            };

        private static Option<bool> VerboseOption() =>
            new(
                aliases: new[] { "-v", "--verbose" },
                description: "Output the log while collecting the gcdump.")
            {
                Argument = new Argument<bool>(name: "verbose")
            };

        public static int DefaultTimeout = 30;
        private static Option<int> TimeoutOption() =>
            new(
                aliases: new[] { "-t", "--timeout" },
                description: $"Give up on collecting the gcdump if it takes longer than this many seconds. The default value is {DefaultTimeout}s.")
            {
                Argument = new Argument<int>(name: "timeout", getDefaultValue: () => DefaultTimeout)
            };

        private static Option<string> DiagnosticPortOption() =>
        new(
            aliases: new[] { "--dport", "--diagnostic-port" },
            description: "The path to a diagnostic port to collect the dump from.")
        {
            Argument = new Argument<string>(name: "diagnostic-port", getDefaultValue: () => string.Empty)
        };
    }
}
