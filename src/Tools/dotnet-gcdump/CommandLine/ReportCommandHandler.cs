// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tools.GCDump.CommandLine;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class ReportCommandHandler
    {
        private delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, FileInfo gcdump_filename, int? processId = null, ReportType reportType = ReportType.HeapStat);

        public static Command ReportCommand() =>
            new(
                name: "report",
                description: "Generate report into stdout from a previously generated gcdump or from a running process.")
            {
                // Handler
                HandlerDescriptor.FromDelegate((ReportDelegate) Report).GetCommandHandler(),
                // Options
                FileNameArgument(), ProcessIdOption(), ReportTypeOption()
            };

        private static Task<int> Report(CancellationToken ct, IConsole console, FileInfo gcdump_filename, int? processId = null, ReportType type = ReportType.HeapStat)
        {
            //
            // Validation
            //
            if (gcdump_filename == null && !processId.HasValue)
            {
                Console.Error.WriteLine("<gcdump_filename> or -p|--process-id is required");
                return Task.FromResult(-1);
            }

            if (gcdump_filename != null && processId.HasValue)
            {
                Console.Error.WriteLine("Specify only one of -f|--file or -p|--process-id.");
                return Task.FromResult(-1);
            }

            ReportSource source = ReportSource.Unknown;

            //
            // Determine report source
            //
            if (gcdump_filename != null)
            {
                source = ReportSource.DumpFile;
            }
            else if (processId.HasValue)
            {
                source = ReportSource.Process;
            }

            return (source, type) switch
            {
                (ReportSource.Process, ReportType.HeapStat) => ReportFromProcess(processId.Value, ct),
                (ReportSource.DumpFile, ReportType.HeapStat) => ReportFromFile(gcdump_filename),
                _ => HandleUnknownParam()
            };
        }

        private static Task<int> HandleUnknownParam()
        {
            Console.Error.WriteLine("Invalid report type and source combination specified.");
            return Task.FromResult(-1);
        }

        private static Task<int> ReportFromProcess(int processId, CancellationToken ct)
        {
            if (!CollectCommandHandler
                .TryCollectMemoryGraph(ct, processId, CollectCommandHandler.DefaultTimeout, false, out Graphs.MemoryGraph mg))
            {
                Console.Error.WriteLine("An error occured while collecting gcdump.");
                return Task.FromResult(-1);
            }

            mg.WriteToStdOut();
            return Task.FromResult(0);
        }

        private static Task<int> ReportFromFile(FileSystemInfo file)
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"Invalid gcdump file {file}");
                return Task.FromResult(-1);
            }

            try
            {
                using FileStream fs = File.OpenRead(file.FullName);
                var dump = new GCHeapDump(fs, file.Name);
                dump.MemoryGraph.WriteToStdOut();
                return Task.FromResult(0);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"An error occured while parsing the file, {e.GetBaseException().Message}");
                return Task.FromResult(-1);
            }
        }

        private static Argument<FileInfo> FileNameArgument() =>
            new Argument<FileInfo>("gcdump_filename")
            {
                Description = "The file to read gcdump from.",
                Arity = new ArgumentArity(0, 1)
            }.ExistingOnly();

        private static Option<int> ProcessIdOption() =>
            new(new[] { "-p", "--process-id" }, "The process id to collect the gcdump from.");

        private static Option<ReportType> ReportTypeOption() =>
            new(new[] { "-t", "--report-type" }, "The type of report to generate. Available options: heapstat (default)")
            {
                Argument = new Argument<ReportType>(() => ReportType.HeapStat)
            };

        private enum ReportSource
        {
            Unknown,
            Process,
            DumpFile,
            DiagServer
        }

        private enum ReportType
        {
            HeapStat
        }
    }
}
