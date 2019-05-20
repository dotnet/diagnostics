using Microsoft.Diagnostics.Tools.RuntimeClient;
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
        public enum DumpTypeOption
        {
            Heap,       // A large and relatively comprehensive dump containing module lists, thread lists, all 
                        // stacks, exception information, handle information, and all memory except for mapped images.
            Mini        // A small dump containing module lists, thread lists, exception information and all stacks.
        }

        public Dumper()
        {
        }

        public async Task<int> Collect(IConsole console, int processId, string output, DumpTypeOption type)
        {
            if (processId == 0) {
                console.Error.WriteLine("ProcessId is required.");
                return 1;
            }

            try
            {
                if (output == null)
                {
                    // Build timestamp based file path
                    string timestamp = $"{DateTime.Now:yyyyMMdd_HHmmss}";
                    output = Path.Combine(Directory.GetCurrentDirectory(), RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"dump_{timestamp}.dmp" : $"core_{timestamp}");
                }
                // Make sure the dump path is NOT relative. This path could be sent to the runtime 
                // process on Linux which may have a different current directory.
                output = Path.GetFullPath(output);

                // Display the type of dump and dump path
                string dumpTypeMessage = type == DumpTypeOption.Mini ? "minidump" : "minidump with heap";
                console.Out.WriteLine($"Writing {dumpTypeMessage} to {output}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Get the process
                    Process process = Process.GetProcessById(processId);

                    await Windows.CollectDumpAsync(process, output, type);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    DiagnosticHelpers.DumpType dumpType = type == DumpTypeOption.Heap ? DiagnosticHelpers.DumpType.WithHeap : DiagnosticHelpers.DumpType.Normal;

                    // Send the command to the runtime to initiate the core dump
                    uint hr = DiagnosticHelpers.GenerateCoreDump(processId, output, dumpType, diagnostics: false);
                    if (hr != 0)
                    {
                        throw new InvalidOperationException($"Core dump generation FAILED 0x{hr:X8}");
                    }
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
