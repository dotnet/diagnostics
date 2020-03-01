// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Tools.Common;
using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class CollectCommandHandler
    {
        delegate Task<int> CollectDelegate(CancellationToken ct, IConsole console, int processId, string output, int timeout, bool verbose);

        /// <summary>
        /// Collects a gcdump from a currently running process.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the gcdump from.</param>
        /// <param name="output">The output path for the collected gcdump.</param>
        /// <returns></returns>
        private static async Task<int> Collect(CancellationToken ct, IConsole console, int processId, string output, int timeout, bool verbose)
        {
            try
            {
                if (processId < 0)
                {
                    Console.Out.WriteLine($"The PID cannot be negative: {processId}");
                    return -1;
                }

                if (processId == 0)
                {
                    Console.Out.WriteLine($"-p|--process-id is required");
                    return -1;
                }
                
                var (success, _) = await CollectGCDump(ct, processId, output, timeout, verbose, true);

                if (success)
                {
                    return 0;
                }

                if (ct.IsCancellationRequested)
                {
                    Console.Out.WriteLine($"\tCancelled.");
                    return -1;
                }

                Console.Out.WriteLine($"\tFailed to collect gcdump. Try running with '-v' for more information.");
                return -1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return -1;
            }
        }

        internal static async Task<(bool Success, FileInfo OutputFile)> CollectGCDump(
            CancellationToken ct,
            int processId, 
            string output, 
            int timeout, 
            bool verbose,
            bool log)
        {
            output = string.IsNullOrEmpty(output)
                ? $"{DateTime.Now:yyyyMMdd\\_hhmmss}_{processId}.gcdump"
                : output;

            var outputFileInfo = new FileInfo(output);

            if (outputFileInfo.Exists)
            {
                outputFileInfo.Delete();
            }

            if (string.IsNullOrEmpty(outputFileInfo.Extension) || outputFileInfo.Extension != ".gcdump")
            {
                outputFileInfo = new FileInfo(outputFileInfo.FullName + ".gcdump");
            }

            if (log) Console.Out.WriteLine($"Writing gcdump to '{outputFileInfo.FullName}'...");

            var dumpTask = Task.Run(() =>
            {
                var memoryGraph = new Graphs.MemoryGraph(50_000);
                var heapInfo = new DotNetHeapInfo();
                if (!EventPipeDotNetHeapDumper.DumpFromEventPipe(ct, processId, memoryGraph,
                    verbose ? Console.Out : TextWriter.Null, timeout, heapInfo))
                    return false;
                memoryGraph.AllowReading();

                GCHeapDump.WriteMemoryGraph(memoryGraph, outputFileInfo.FullName, "dotnet-gcdump");
                return true;
            }, ct);

            var fDumpSuccess = await dumpTask;
            if (fDumpSuccess)
            {
                outputFileInfo.Refresh();
                if (log) Console.Out.WriteLine($"Finished writing {outputFileInfo.Length} bytes to {outputFileInfo.FullName}");
            }
            return (fDumpSuccess, outputFileInfo); 
        }

        public static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Collects a diagnostic trace from a currently running process")
            {
                // Handler
                HandlerDescriptor.FromDelegate((CollectDelegate) Collect).GetCommandHandler(),
                // Options
                ProcessIdOption(), OutputPathOption(), VerboseOption(), TimeoutOption()
            };

        private static Option ProcessIdOption() =>
            new Option(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect the trace.")
            {
                Argument = new Argument<int>(name: "pid", defaultValue: 0),
            };

        private static Option OutputPathOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: $@"The path where collected gcdumps should be written. Defaults to '.\YYYYMMDD_HHMMSS_<pid>.gcdump' where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path and file name of the dump.")
            {
                Argument = new Argument<string>(name: "gcdump-file-path", defaultValue: "")
            };

        private static Option VerboseOption() =>
            new Option(
                aliases: new[] { "-v", "--verbose" },
                description: $"Output the log while collecting the gcdump.") 
            {
                Argument = new Argument<bool>(name: "verbose", defaultValue: false)
            };

        public static int DefaultTimeout = 30;
        private static Option TimeoutOption() =>
            new Option(
                aliases: new[] { "-t", "--timeout" },
                description: $"Give up on collecting the gcdump if it takes longer than this many seconds. The default value is {DefaultTimeout}s.")
            {
                Argument = new Argument<int>(name: "timeout", defaultValue: DefaultTimeout)
            };
    }
}
