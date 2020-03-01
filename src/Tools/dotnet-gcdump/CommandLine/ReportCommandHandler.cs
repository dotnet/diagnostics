using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class ReportCommandHandler
    {
        delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, FileInfo file = null, int? processId = null, ReportType reportType = ReportType.HeapStat);
        
        public static Command ReportCommand() =>
            new Command(
                name: "report",
                description: "Generate report into stdout from a previously generated gcdump or from a running process.")
            {
                // Handler
                HandlerDescriptor.FromDelegate((ReportDelegate) Report).GetCommandHandler(),
                // Options
                FileNameOption(), ProcessIdOption(), ReportTypeOption()
            };

        private static Task<int> Report(CancellationToken ct, IConsole console, FileInfo file = null, int? processId = null, ReportType type = ReportType.HeapStat)
        {
            //
            // Validation
            //
            if (file == null && !processId.HasValue)
            {
                Console.Error.WriteLine("-f|--file or -p|process-id is required");
                return Task.FromResult(-1);
            }
            
            if (file != null && processId.HasValue)
            {
                Console.Error.WriteLine("Specify one of -f|--file or -p|process-id.");
                return Task.FromResult(-1);
            }

            var source = ReportSource.Unknown;

            //
            // Determine report source
            //
            if (file != null)
                source = ReportSource.DumpFile;
            else if (processId.HasValue)
                source = ReportSource.Process;

            return (source, type) switch
            {
                (ReportSource.Process, ReportType.HeapStat)  => ReportFromProcess(processId.Value, ct),
                (ReportSource.DumpFile, ReportType.HeapStat) => ReportFromFile(file),
                _                                            => HandleUnknownParam()
            };
        }

        private static Task<int> HandleUnknownParam()
        {
            Console.Error.WriteLine("Invalid report type and source combination specified.");
            return Task.FromResult(1);
        }

        private static async Task<int> ReportFromProcess(int processId, CancellationToken ct)
        {
            var tempFile = Path.GetTempFileName();

            try
            {
                var (success, outputFile) = await CollectCommandHandler.CollectGCDump(ct, processId, tempFile, CollectCommandHandler.DefaultTimeout, false, false);
                if (!success)
                {
                    Console.Error.WriteLine("An error occured while collecting gcdump.");
                    return -1;
                }

                var result = await ReportFromFile(outputFile);
                return result;
            }
            finally
            {
                File.Delete(tempFile);                
            }
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
                var dump = GCHeapDump.ReadMemoryGraph(file.FullName);
                dump.MemoryGraph.WriteToStdOut();
                return Task.FromResult(0);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"An error occured while parsing the file, {e.GetBaseException().Message}");
                return Task.FromResult(-1);
            }
        }

        private static Option<FileInfo> FileNameOption() =>
            new Option<FileInfo>(new[] {"-f", "--file"}, "The file to read gcdump from.");
        
        private static Option<int> ProcessIdOption() =>
            new Option<int>(new[] { "-p", "--process-id" }, "The process id to collect the trace.");
        
        private static Option<ReportType> ReportTypeOption() =>
            new Option<ReportType>(new[] { "-t", "--report-type" }, "The type of report to generate. Available options: heapstat (default)")
            {
                Argument = new Argument<ReportType>(() => ReportType.HeapStat)
            }
        ;

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