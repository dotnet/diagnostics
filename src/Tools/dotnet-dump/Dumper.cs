using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Microsoft.Diagnostic.Tools.Dump
{
    public partial class Dumper
    {
        public Dumper()
        {
        }

        public async Task<int> Collect(IConsole console, int processId, string outputDirectory)
        {
            if (processId == 0) {
                console.Error.WriteLine("ProcessId is required.");
                return 1;
            }

            // System.CommandLine has a bug in the default value handling
            if (outputDirectory == null) {
                outputDirectory = Directory.GetCurrentDirectory();
            }

            // Get the process
            Process process = null;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                console.Error.WriteLine($"Invalid process id: {processId}");
                return 1;
            }

            // Generate the file name
            string fileName = Path.Combine(outputDirectory, $"{process.ProcessName}-{process.Id}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.dmp");

            console.Out.WriteLine($"Collecting memory dump for {process.ProcessName} (ID: {process.Id}) ...");
    
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                await Windows.CollectDumpAsync(process, fileName);
            }
            else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                await Linux.CollectDumpAsync(process, fileName);
            }
            else {
                console.Error.WriteLine($"Unsupported operating system {RuntimeInformation.OSDescription}");
                return 1;
            }

            console.Out.WriteLine($"Dump saved to {fileName}");
            return 0;
        }
    }
}
