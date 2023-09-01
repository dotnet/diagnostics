// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Dump
{
    public partial class Dumper
    {
        /// <summary>
        /// The dump type determines the kinds of information that are collected from the process.
        /// </summary>
        public enum DumpTypeOption
        {
            Full,       // The largest dump containing all memory including the module images.

            Heap,       // A large and relatively comprehensive dump containing module lists, thread lists, all
                        // stacks, exception information, handle information, and all memory except for mapped images.

            Mini,       // A small dump containing module lists, thread lists, exception information and all stacks.

            Triage      // A small dump containing module lists, thread lists, exception information, all stacks and PII removed.
        }

        public Dumper()
        {
        }

        public int Collect(IConsole console, int processId, string output, bool diag, bool crashreport, DumpTypeOption type, string name)
        {
            Console.WriteLine(name);
            if (name != null)
            {
                if (processId != 0)
                {
                    Console.WriteLine("Can only specify either --name or --process-id option.");
                    return -1;
                }
                processId = CommandUtils.FindProcessIdWithName(name);
                if (processId < 0)
                {
                    return -1;
                }
            }

            if (processId == 0)
            {
                Console.Error.WriteLine("ProcessId is required.");
                return -1;
            }

            if (processId < 0)
            {
                Console.Error.WriteLine($"The PID cannot be negative: {processId}");
                return -1;
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
                string dumpTypeMessage = null;
                switch (type)
                {
                    case DumpTypeOption.Full:
                        dumpTypeMessage = "full";
                        break;
                    case DumpTypeOption.Heap:
                        dumpTypeMessage = "dump with heap";
                        break;
                    case DumpTypeOption.Mini:
                        dumpTypeMessage = "dump";
                        break;
                    case DumpTypeOption.Triage:
                        dumpTypeMessage = "triage dump";
                        break;
                }
                console.Out.WriteLine($"Writing {dumpTypeMessage} to {output}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (crashreport)
                    {
                        Console.WriteLine("Crash reports not supported on Windows.");
                        return -1;
                    }

                    Windows.CollectDump(processId, output, type);
                }
                else
                {
                    DiagnosticsClient client = new(processId);

                    DumpType dumpType = DumpType.Normal;
                    switch (type)
                    {
                        case DumpTypeOption.Full:
                            dumpType = DumpType.Full;
                            break;
                        case DumpTypeOption.Heap:
                            dumpType = DumpType.WithHeap;
                            break;
                        case DumpTypeOption.Mini:
                            dumpType = DumpType.Normal;
                            break;
                        case DumpTypeOption.Triage:
                            dumpType = DumpType.Triage;
                            break;
                    }

                    WriteDumpFlags flags = WriteDumpFlags.None;
                    if (diag)
                    {
                        flags |= WriteDumpFlags.LoggingEnabled;
                    }
                    if (crashreport)
                    {
                        flags |= WriteDumpFlags.CrashReportEnabled;
                    }
                    // Send the command to the runtime to initiate the core dump
                    client.WriteDump(dumpType, output, flags);
                }
            }
            catch (Exception ex) when
                (ex is FileNotFoundException or
                 ArgumentException or
                 DirectoryNotFoundException or
                 UnauthorizedAccessException or
                 PlatformNotSupportedException or
                 UnsupportedCommandException or
                 InvalidDataException or
                 InvalidOperationException or
                 NotSupportedException or
                 DiagnosticsClientException)
            {
                console.Error.WriteLine($"{ex.Message}");
                return -1;
            }

            console.Out.WriteLine($"Complete");
            return 0;
        }
    }
}
