// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Graphs;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class CollectCommandHandler
    {
        private delegate Task<int> CollectDelegate(CancellationToken ct, IConsole console, int processId, string output, int timeout, bool verbose, string name);

        /// <summary>
        /// Collects a gcdump from a currently running process.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the gcdump from.</param>
        /// <param name="output">The output path for the collected gcdump.</param>
        /// <returns></returns>
        private static async Task<int> Collect(CancellationToken ct, IConsole console, int processId, string output, int timeout, bool verbose, string name)
        {
            if (name != null)
            {
                if (processId != 0)
                {
                    Console.WriteLine("Can only specify either --name or --process-id option.");
                    return -1;
                }
                processId = CommandUtils.FindProcessIdWithName(name);
                if (processId < 0)
                {
                    return -1;
                }
            }

            try
            {
                if (processId < 0)
                {
                    Console.Out.WriteLine($"The PID cannot be negative: {processId}");
                    return -1;
                }

                if (processId == 0)
                {
                    Console.Out.WriteLine("-p|--process-id is required");
                    return -1;
                }

                output = string.IsNullOrEmpty(output)
                    ? $"{DateTime.Now:yyyyMMdd\\_HHmmss}_{processId}.gcdump"
                    : output;

                FileInfo outputFileInfo = new FileInfo(output);

                if (outputFileInfo.Exists)
                {
                    outputFileInfo.Delete();
                }

                if (string.IsNullOrEmpty(outputFileInfo.Extension) || outputFileInfo.Extension != ".gcdump")
                {
                    outputFileInfo = new FileInfo(outputFileInfo.FullName + ".gcdump");
                }

                Console.Out.WriteLine($"Writing gcdump to '{outputFileInfo.FullName}'...");

                var dumpTask = Task.Run(() => {
                    if (TryCollectMemoryGraph(ct, processId, timeout, verbose, out MemoryGraph memoryGraph))
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
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return -1;
            }
        }

        internal static bool TryCollectMemoryGraph(CancellationToken ct, int processId, int timeout, bool verbose,
            out MemoryGraph memoryGraph)
        {
            var heapInfo = new DotNetHeapInfo();
            TextWriter log = verbose ? Console.Out : TextWriter.Null;

            memoryGraph = new MemoryGraph(50_000);

            if (!EventPipeDotNetHeapDumper.DumpFromEventPipe(ct, processId, memoryGraph, log, timeout, heapInfo))
            {
                return false;
            }

            memoryGraph.AllowReading();
            return true;
        }

        public static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Collects a diagnostic trace from a currently running process")
            {
                // Handler
                HandlerDescriptor.FromDelegate((CollectDelegate) Collect).GetCommandHandler(),
                // Options
                ProcessIdOption(), OutputPathOption(), VerboseOption(), TimeoutOption(), NameOption()
            };

        private static Option ProcessIdOption() =>
            new Option(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect the gcdump from.")
            {
                Argument = new Argument<int>(name: "pid"),
            };

        private static Option NameOption() =>
            new Option(
                aliases: new[] { "-n", "--name" },
                description: "The name of the process to collect the gcdump from.")
            {
                Argument = new Argument<string>(name: "name")
            };

        private static Option OutputPathOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: $@"The path where collected gcdumps should be written. Defaults to '.\YYYYMMDD_HHMMSS_<pid>.gcdump' where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path and file name of the dump.")
            {
                Argument = new Argument<string>(name: "gcdump-file-path", getDefaultValue: () => string.Empty)
            };

        private static Option VerboseOption() =>
            new Option(
                aliases: new[] { "-v", "--verbose" },
                description: "Output the log while collecting the gcdump.")
            {
                Argument = new Argument<bool>(name: "verbose")
            };

        public static int DefaultTimeout = 30;
        private static Option TimeoutOption() =>
            new Option(
                aliases: new[] { "-t", "--timeout" },
                description: $"Give up on collecting the gcdump if it takes longer than this many seconds. The default value is {DefaultTimeout}s.")
            {
                Argument = new Argument<int>(name: "timeout", getDefaultValue: () => DefaultTimeout)
            };
    }
}
