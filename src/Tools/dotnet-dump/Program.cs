using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Internal.Utilities;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "dotnet-dump", Description = "Captures memory dumps of .NET processes")]
    internal class Program
    {
        [Required(ErrorMessage = "You must provide a process ID to be dumped.")]
        [Option("-p|--process-id <PROCESS_ID>", Description = "The ID of the process to collect a memory dump for")]
        public int ProcessId { get; set; }

        [Option("-o|--output <OUTPUT_DIRECTORY>", Description = "The directory to write the dump to. Defaults to the current working directory.")]
        public string OutputDir { get; set; }

        public async Task<int> OnExecute(IConsole console, CommandLineApplication app)
        {
            if (string.IsNullOrEmpty(OutputDir))
            {
                OutputDir = Directory.GetCurrentDirectory();
            }

            // Get the process
            var process = Process.GetProcessById(ProcessId);

            // Generate the file name
            var fileName = Path.Combine(OutputDir, $"{process.ProcessName}-{process.Id}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.dmp");

            console.WriteLine($"Collecting memory dump for {process.ProcessName} (ID: {process.Id}) ...");
            await Dumper.CollectDumpAsync(process, fileName);
            console.WriteLine($"Dump saved to {fileName}");

            return 0;
        }

        private static int Main(string[] args)
        {
            DebugUtil.WaitForDebuggerIfRequested(ref args);

            try
            {
                return CommandLineApplication.Execute<Program>(args);
            }
            catch(PlatformNotSupportedException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }
    }
}
