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
        /// <summary>
        /// The dump type determines the kinds of information that are collected from the process.
        /// </summary>
        public enum DumpType
        {
            Heap,       // A large and relatively comprehensive dump containing module lists, thread lists, all 
                        // stacks, exception information, handle information, and all memory except for mapped images.
            Mini        // A small dump containing module lists, thread lists, exception information and all stacks.
        }

        public Dumper()
        {
        }

        public async Task<int> Collect(IConsole console, int processId, string output, DumpType type)
        {
            if (processId == 0) {
                console.Error.WriteLine("ProcessId is required.");
                return 1;
            }

            try
            {
                // Get the process
                Process process = Process.GetProcessById(processId);

                if (output == null)
                {
                    // Build timestamp based file path
                    string timestamp = $"{DateTime.Now:yyyyMMdd_HHmmss}";
                    output = Path.Combine(Directory.GetCurrentDirectory(), RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"dump_{timestamp}.dmp" : $"core_{timestamp}");
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Matches createdump's output on Linux
                    string dumpType = type == DumpType.Mini ? "minidump" : "minidump with heap";
                    console.Out.WriteLine($"Writing {dumpType} to {output}");

                    await Windows.CollectDumpAsync(process, output, type);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    await Linux.CollectDumpAsync(process, output, type);
                }
                else {
                    throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
                }
            }
            catch (Exception ex) when 
                (ex is FileNotFoundException || 
                 ex is DirectoryNotFoundException || 
                 ex is UnauthorizedAccessException || 
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
