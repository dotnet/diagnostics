// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Graphs;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class CollectCommandHandler
    {
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
        private static async Task<int> Collect(CancellationToken ct, int processId, string output, int timeout, bool verbose, string name, string diagnosticPort, string dsrouter)
        {
            if (!CommandUtils.ResolveProcessForAttach(processId, name, diagnosticPort, dsrouter, out int resolvedProcessId))
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

        public static Command CollectCommand()
        {
            Command collectCommand = new(
                name: "collect",
                description: "Collects a diagnostic trace from a currently running process")
            {
                ProcessIdOption,
                OutputPathOption,
                VerboseOption,
                TimeoutOption,
                NameOption,
                DiagnosticPortOption
            };

            collectCommand.SetAction(static (parseResult, ct) => Collect(ct,
                    processId: parseResult.GetValue(ProcessIdOption),
                    output: parseResult.GetValue(OutputPathOption) ?? string.Empty,
                    timeout: parseResult.GetValue(TimeoutOption),
                    verbose: parseResult.GetValue(VerboseOption),
                    name: parseResult.GetValue(NameOption),
                    diagnosticPort: parseResult.GetValue(DiagnosticPortOption) ?? string.Empty,
                    dsrouter: string.Empty));

            return collectCommand;
        }

        private static readonly Option<int> ProcessIdOption =
            new("--process-id", "-p")
            {
                Description = "The process id to collect the gcdump from."
            };

        private static readonly Option<string> NameOption =
            new("--name", "-n")
            {
                Description = "The name of the process to collect the gcdump from."
            };

        private static readonly Option<string> OutputPathOption =
            new("--output", "-o")
            {
                Description = @"The path where collected gcdumps should be written. Defaults to '.\YYYYMMDD_HHMMSS_<pid>.gcdump' where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path and file name of the dump."
            };

        private static readonly Option<bool> VerboseOption =
            new("--verbose", "-v")
            {
                Description = "Output the log while collecting the gcdump."
            };

        public static int DefaultTimeout = 30;
        private static readonly Option<int> TimeoutOption =
            new("--timeout", "-t")
            {
                Description = $"Give up on collecting the gcdump if it takes longer than this many seconds. The default value is {DefaultTimeout}s.",
                DefaultValueFactory = _ => DefaultTimeout,
            };

        private static readonly Option<string> DiagnosticPortOption =
            new("--diagnostic-port", "--dport")
            {
                Description = "The path to a diagnostic port to collect the dump from."
            };
    }
}
