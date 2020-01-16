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
        delegate Task<int> CollectDelegate(CancellationToken ct, IConsole console, int processId, string transportPath, string output, int timeout, bool verbose);

        /// <summary>
        /// Collects a gcdump from a currently running process.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the gcdump from.</param>
        /// <param name="output">The output path for the collected gcdump.</param>
        /// <returns></returns>
        private static async Task<int> Collect(CancellationToken ct, IConsole console, int processId, string transportPath, string output, int timeout, bool verbose)
        {
            try
            {
                if (string.IsNullOrEmpty(transportPath))
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
                }
                else
                {
                    if (!File.Exists(transportPath) && !File.Exists(@"\\.\pipe\" + transportPath))
                    {
                        Console.Error.WriteLine("Requested transport does not exist");
                        return -1;
                    }
                    else if (processId != 0)
                    {
                        Console.Error.WriteLine("Cannot specify both a PID and a specific transport");
                        return -1;
                    }
                }

                output = string.IsNullOrEmpty(output) ? 
                    $"{DateTime.Now.ToString(@"yyyyMMdd\_hhmmss")}_{(processId != 0 ? processId.ToString() : (new FileInfo(transportPath)).Name)}.gcdump" :
                    output;

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
                var dumpTask = Task.Run(() => 
                {
                    var memoryGraph = new Graphs.MemoryGraph(50_000);
                    var heapInfo = new DotNetHeapInfo();
                    if (!EventPipeDotNetHeapDumper.DumpFromEventPipe(
                            ct,
                            processId != 0 ? processId.ToString() : transportPath,
                            memoryGraph,
                            verbose ? Console.Out : TextWriter.Null,
                            timeout,
                            heapInfo))
                    {
                        return false;
                    }
                    memoryGraph.AllowReading();
                    GCHeapDump.WriteMemoryGraph(memoryGraph, outputFileInfo.FullName, "dotnet-gcdump");
                    return true;
                });

                var fDumpSuccess = await dumpTask;

                if (fDumpSuccess)
                {
                    outputFileInfo.Refresh();
                    Console.Out.WriteLine($"\tFinished writing {outputFileInfo.Length} bytes.");
                    return 0;
                }
                else if (ct.IsCancellationRequested)
                {
                    Console.Out.WriteLine($"\tCancelled.");
                    return -1;
                }
                else
                {
                    Console.Out.WriteLine($"\tFailed to collect gcdump. Try running with '-v' for more information.");
                    return -1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return -1;
            }
        }

        public static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Collects a diagnostic trace from a currently running process")
            {
                // Handler
                HandlerDescriptor.FromDelegate((CollectDelegate)Collect).GetCommandHandler(),
                // Options
                ProcessIdOption(), TransportPathOption(), OutputPathOption(), VerboseOption(), TimeoutOption() 
            };

        public static Option ProcessIdOption() =>
            new Option(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect the trace.")
            {
                Argument = new Argument<int>(name: "pid", defaultValue: 0),
            };

        public static Option TransportPathOption() =>
            new Option(
                aliases: new[] { "--transport-path" },
                description: "A fully qualified path and filename for the OS transport to communicate over.  Supersedes the pid argument if provided.")
            {
                Argument = new Argument<string>(name: "transportPath"),
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

        private static int DefaultTimeout = 30;
        private static Option TimeoutOption() =>
            new Option(
                aliases: new[] { "-t", "--timeout" },
                description: $"Give up on collecting the gcdump if it takes longer than this many seconds. The default value is {DefaultTimeout}s.")
            {
                Argument = new Argument<int>(name: "timeout", defaultValue: DefaultTimeout)
            };
    }
}
