using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostic.Tools.Dump
{
    public partial class Dumper
    {
        public Dumper()
        {
        }

        public async Task<int> Collect(IConsole console, int processId, int intervalSec, int number, string output, string type)
        {
            if (processId == 0) {
                console.Error.WriteLine("ProcessId is required.");
                return 1;
            }

            if (type != "heap" && type != "triage") {
                console.Error.WriteLine($"Invalid Dump type '{type}'. Must be either 'heap' or 'triage'.");
                return 1;
            }

            try
            {
                // Get the process
                Process process = Process.GetProcessById(processId);

                bool isDirectory = Directory.Exists(output) || output.EndsWith(Path.DirectorySeparatorChar) || number != 0 || intervalSec != 0;
                bool triage = type == "triage";
                number = number == 0 ? 1 : number;
                intervalSec = intervalSec == 0 ? 10 : intervalSec;

                string filePath = null;
                if (isDirectory)
                {
                    // Output is a directory
                    Directory.CreateDirectory(output);
                }
                else {
                    // Output is the file path
                    filePath = output;
                }

                for (int n = 0; n < number; n++)
                {
                    if (isDirectory)
                    {
                        // Build timestamp based file path
                        string timestamp = $"{DateTime.Now:yyyyMMdd_HHmmss}";
                        filePath = Path.Combine(output, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"dump_{timestamp}.dmp" : $"core_{timestamp}");
                    }

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                        // Display what createdump does on Linux
                        string dumpType = triage ? "triage minidump" : "minidump with heap";
                        console.Out.WriteLine($"Writing {dumpType} to {filePath}");

                        await Windows.CollectDumpAsync(process, filePath, triage);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                        await Linux.CollectDumpAsync(process, filePath, triage);
                    }
                    else {
                        throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
                    }

                    if ((n + 1) < number) {
                        Thread.Sleep(intervalSec * 1000);
                    }
                }
            }
            catch (Exception ex) when 
                (ex is IOException || 
                 ex is UnauthorizedAccessException || 
                 ex is ArgumentException || 
                 ex is PlatformNotSupportedException || 
                 ex is InvalidDataException ||
                 ex is InvalidOperationException ||
                 ex is NotSupportedException)
            {
                console.Error.WriteLine($"{ex.Message}");
                return 1;
            }

            console.Out.WriteLine($"Complete");
            return 0;
        }
    }
}
