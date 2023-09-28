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
using Microsoft.Internal.Common.Utils;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class ReportCommandHandler
    {
        private delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, FileInfo gcdump_filename, int? processId = null, ReportType reportType = ReportType.HeapStat, string diagnosticPort = null);

        public static Command ReportCommand() =>
            new(
                name: "report",
                description: "Generate report into stdout from a previously generated gcdump or from a running process.")
            {
                // Handler
                HandlerDescriptor.FromDelegate((ReportDelegate) Report).GetCommandHandler(),
                // Options
                FileNameArgument(),
                ProcessIdOption(),
                ReportTypeOption(),
                DiagnosticPortOption(),
            };

        private static Task<int> Report(CancellationToken ct, IConsole console, FileInfo gcdump_filename, int? processId = null, ReportType type = ReportType.HeapStat, string diagnosticPort = null)
        {
            //
            // Validation
            //
            if (gcdump_filename == null && !processId.HasValue && string.IsNullOrEmpty(diagnosticPort))
            {
                Console.Error.WriteLine("<gcdump_filename> or -p|--process-id or --dport|--diagnostic-port is required");
                return Task.FromResult(-1);
            }

            if (gcdump_filename != null && (processId.HasValue || !string.IsNullOrEmpty(diagnosticPort)))
            {
                Console.Error.WriteLine("Specify only one of -f|--file or -p|--process-id or --dport|--diagnostic-port.");
                return Task.FromResult(-1);
            }

            if (processId.HasValue && !string.IsNullOrEmpty(diagnosticPort))
            {
                Console.Error.WriteLine("Specify only one of -p|--process-id or -dport|--diagnostic-port.");
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
            else if (processId.HasValue || !string.IsNullOrEmpty(diagnosticPort))
            {
                source = ReportSource.Process;
            }

            return (source, type) switch
            {
                (ReportSource.Process, ReportType.HeapStat) => ReportFromProcess(processId ?? 0, diagnosticPort, ct),
                (ReportSource.DumpFile, ReportType.HeapStat) => ReportFromFile(gcdump_filename),
                _ => HandleUnknownParam()
            };
        }

        private static Task<int> HandleUnknownParam()
        {
            Console.Error.WriteLine("Invalid report type and source combination specified.");
            return Task.FromResult(-1);
        }

        private static Task<int> ReportFromProcess(int processId, string diagnosticPort, CancellationToken ct)
        {
            if (!CommandUtils.ValidateArgumentsForAttach(processId, string.Empty, diagnosticPort, out int resolvedProcessId))
            {
                return Task.FromResult(-1);
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
                        return Task.FromResult(-1);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"--diagnostic-port argument error: {ex.Message}");
                    return Task.FromResult(-1);
                }

                processId = 0;
            }

            if (!CollectCommandHandler
                .TryCollectMemoryGraph(ct, processId, diagnosticPort, CollectCommandHandler.DefaultTimeout, false, out Graphs.MemoryGraph mg))
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
                GCHeapDump dump = new(fs, file.Name);
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
            new(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect the gcdump from.")
            {
                Argument = new Argument<int>(name: "pid"),
            };

        private static Option<ReportType> ReportTypeOption() =>
            new(
                aliases: new[] { "-t", "--report-type" },
                description: "The type of report to generate. Available options: heapstat (default)")
            {
                Argument = new Argument<ReportType>(name: "report-type", () => ReportType.HeapStat)
            };

        private static Option<string> DiagnosticPortOption() =>
            new(
                aliases: new[] { "--dport", "--diagnostic-port" },
                description: "The path to a diagnostic port to collect the dump from.")
            {
                Argument = new Argument<string>(name: "diagnostic-port", getDefaultValue: () => string.Empty)
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
