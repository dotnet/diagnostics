using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Rendering;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class ReportCommandHandler
    {
        delegate Task<int> PrintDelegate(CancellationToken ct, IConsole console, FileInfo file = null, int? processId = null);
        
        public static Command ReportCommand() =>
            new Command(
                name: "report",
                description: "Generate report into stdout from a previously generated gcdump or from a running process.")
            {
                // Handler
                HandlerDescriptor.FromDelegate((PrintDelegate) Report).GetCommandHandler(),
                // Options
                FileNameOption(), ProcessIdOption()
            };

        private static Task<int> Report(CancellationToken ct, IConsole console, FileInfo file = null, int? processId = null)
        {
            if (file == null && !processId.HasValue)
            {
                Console.Error.WriteLine("-f|--file or -p|process-id is required");
                return Task.FromResult(-1);
            }

            return file != null 
                ? ReportFromFile(file) 
                : ReportFromProcess(processId.Value, ct);
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

        private static Task<int> ReportFromFile(FileInfo file)
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
    }
}