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

        public enum LogLevelOption
        {
            None,       // No logging.
            Diag,       // Diagnostic logging, useful for first level.
            Verbose,    // Verbose loggging, useful to debug unwinding but also slows down target process.
        }

        internal static int Collect(DumpCollectionConfig config, IConsole console)
        {
            if (!CommandUtils.ValidateArgumentsForAttach(config.ProcessId, config.ProcessName, config.DiagnosticPort, out int targetProcessId))
            {
                return -1;
            }

            try
            {
                if (config.DumpOutputPath is null)
                {
                    // Build timestamp based file path
                    string timestamp = $"{DateTime.Now:yyyyMMdd_HHmmss}";
                    config.DumpOutputPath = Path.Combine(Directory.GetCurrentDirectory(), RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"dump_{timestamp}.dmp" : $"core_{timestamp}");
                }
                else
                {
                    // Make sure the dump path is NOT relative. This path could be sent to the runtime
                    // process on Linux which may have a different current directory.
                    config.DumpOutputPath = Path.GetFullPath(config.DumpOutputPath);
                }

                string dumpTypeMessage = config.DumpType switch
                {
                    DumpTypeOption.Full => "full",
                    DumpTypeOption.Heap => "dump with heap",
                    DumpTypeOption.Mini => "dump",
                    DumpTypeOption.Triage => "triage dump",
                    _ => throw new ArgumentException("Invalid dump type.")
                };

                console.Out.WriteLine($"Writing {dumpTypeMessage} to {config.DumpOutputPath}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (config.GenerateCrashReport)
                    {
                        console.Error.WriteLine("Crash reports not supported on Windows.");
                        return -1;
                    }

                    if (config.LogLevel != LogLevelOption.None)
                    {
                        console.Error.WriteLine("Diagnostic logging not supported on Windows.");
                        return -1;
                    }

                    Windows.CollectDump(targetProcessId, config.DumpOutputPath, config.DumpType);
                }
                else
                {
                    DumpType dumpType = config.DumpType switch
                    {
                        DumpTypeOption.Full => DumpType.Full,
                        DumpTypeOption.Heap => DumpType.WithHeap,
                        DumpTypeOption.Mini => dumpType = DumpType.Normal,
                        DumpTypeOption.Triage => DumpType.Triage,
                        _ => throw new ArgumentException("Invalid dump type.")
                    };

                    DiagnosticsClient client;
                    if (!string.IsNullOrEmpty(config.DiagnosticPort))
                    {
                        IpcEndpointConfig portConfig = IpcEndpointConfig.Parse(config.DiagnosticPort);
                        if (portConfig.IsListenConfig)
                        {
                            console.Error.WriteLine("dotnet-dump only supports connect mode to a runtime.");
                            return -1;
                        }

                        client = new DiagnosticsClient(portConfig);
                    }
                    else
                    {
                        client = new DiagnosticsClient(targetProcessId);
                    }

                    WriteDumpFlags flags = WriteDumpFlags.None;

                    flags |= config.LogLevel switch
                    {
                        LogLevelOption.None => WriteDumpFlags.None,
                        LogLevelOption.Diag => WriteDumpFlags.LoggingEnabled,
                        LogLevelOption.Verbose => WriteDumpFlags.VerboseLoggingEnabled,
                        _ => throw new ArgumentException($"Invalid log level supplied: {config.LogLevel}")
                    };

                    if (config.LogToFile || config.DiagnosticLogPath is not null)
                    {
                        flags |= WriteDumpFlags.LogToFile;
                    }
                    else if (flags != WriteDumpFlags.None)
                    {
                        console.Out.WriteLine("Diagnostic output requested. Logging will appear in the console of the target process.");
                    }

                    if (config.GenerateCrashReport)
                    {
                        flags |= WriteDumpFlags.CrashReportEnabled;
                    }

                    // Send the command to the runtime to initiate the core dump
                    client.WriteDump(dumpType, config.DumpOutputPath, flags, config.DiagnosticLogPath);
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
                if (config.LogLevel == LogLevelOption.None)
                {
                    console.Error.WriteLine($"Consider rerunning the command with diagnostic output enabled.");
                }
                return -1;
            }

            console.Out.WriteLine($"Complete");
            return 0;
        }
    }
}
