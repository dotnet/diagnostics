// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Dump
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

        public async Task<int> Collect(IConsole console, int processId, string diagnosticsServerAddress, string output, bool diag, DumpTypeOption type)
        {
            if (string.IsNullOrEmpty(diagnosticsServerAddress) && processId == 0)
            {
                console.Error.WriteLine("ProcessId is required.");
                return 1;
            }
            else
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Console.Error.WriteLine("Cannot create dumps via the Diagnostics Server on Windows");
                    return 1;
                }
                else if (!File.Exists(diagnosticsServerAddress) && !File.Exists(@"\\.\pipe\" + diagnosticsServerAddress))
                {
                    Console.Error.WriteLine("Requested transport does not exist");
                    return 1;
                }
                else if (processId != 0)
                {
                    Console.Error.WriteLine("Cannot specify both a PID and specific transport");
                    return 1;
                }
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
                string dumpTypeMessage = type == DumpTypeOption.Mini ? "dump" : "dump with heap";
                console.Out.WriteLine($"Writing {dumpTypeMessage} to {output}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Get the process
                    Process process = Process.GetProcessById(processId);

                    await Windows.CollectDumpAsync(process, output, type);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var client = string.IsNullOrEmpty(diagnosticsServerAddress) ? new DiagnosticsClient(processId) : new DiagnosticsClient(diagnosticsServerAddress);
                    DumpType dumpType = type == DumpTypeOption.Heap ? DumpType.WithHeap : DumpType.Normal;

                    // Send the command to the runtime to initiate the core dump
                    client.WriteDump(dumpType, output, diag);
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
                 ex is NotSupportedException ||
                 ex is DiagnosticsClientException)
            {
                console.Error.WriteLine($"{ex.Message}");
                return 1;
            }

            console.Out.WriteLine($"Complete");
            return 0;
        }
    }
}
