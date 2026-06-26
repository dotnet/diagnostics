// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.StressLogs;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumplog", Aliases = new[] { "DumpLog" }, Help = "Writes the contents of an in-memory stress log to a file.")]
    public sealed class DumpLogCommand : ClrRuntimeCommandBase
    {
        [Argument(Name = "filename", Help = "The file to write the stress log to. Defaults to StressLog.txt in the current directory.")]
        public string FileName { get; set; }

        [Option(Name = "-addr", Help = "The hex address of a StressLog to dump instead of the runtime's own stress log.")]
        public string AddressString { get; set; }

        // The sentinel ClrMD's stress-log reader emits for a message whose format
        // string could not be read from the target. When the runtime's read-only
        // data (where the format strings live) is missing from the dump and the
        // runtime binary cannot be located, every message renders this way.
        private const string UnresolvedFormatMarker = "<unresolved-format>";

        // Warn only when an overwhelming majority is unresolved; a handful of
        // unresolved formats is normal (e.g. a format string in a module that is
        // genuinely absent from both the dump and the symbol path).
        private const int UnresolvedWarningThresholdPercent = 50;

        public override void Invoke()
        {
            string fileName = string.IsNullOrWhiteSpace(FileName) ? "StressLog.txt" : FileName;

            StressLog stressLog;
            bool ownsStressLog = false;
            if (!string.IsNullOrWhiteSpace(AddressString))
            {
                if (!TryParseAddress(AddressString, out ulong address))
                {
                    WriteLineError($"Could not parse stress log address '{AddressString}'.");
                    return;
                }

                if (!StressLog.TryOpen(Runtime.DataTarget, address, out stressLog, out string addressFailure))
                {
                    WriteLineError($"{addressFailure}");
                    return;
                }

                ownsStressLog = true;
            }
            else if (!Runtime.TryGetStressLog(out stressLog, out string failureReason))
            {
                WriteLineError($"{failureReason}");
                return;
            }

            try
            {
                WriteLine($"Attempting to dump Stress log to file '{fileName}'");

                // First pass: thread count and total elapsed time, needed for the header.
                CountThreadsAndElapsed(stressLog, Console.CancellationToken, out int threadCount, out double elapsedSeconds);

                int pointerHexDigits = stressLog.PointerSize * 2;
                int messageCount = 0;
                int unresolvedCount = 0;
                bool interrupted = false;

                using (StreamWriter writer = new(fileName))
                {
                    StressLogFormat.WriteHeader(writer, stressLog, threadCount, elapsedSeconds);
                    writer.WriteLine();

                    TextWriterConsole tableConsole = new(writer, Console.CancellationToken);
                    Table output = new(tableConsole,
                        new Column(Align.Left, 6, Formats.Text),    // THREAD (hex thread id)
                        new Column(Align.Right, 16, Formats.Text),  // TIMESTAMP (seconds from start)
                        new Column(Align.Left, 20, Formats.Text),   // FACILITY
                        ColumnKind.Text);                           // MESSAGE
                    output.WriteHeader("THREAD", "TIMESTAMP", "FACILITY", "MESSAGE");

                    foreach (StressLogMessage message in stressLog.EnumerateMessages(Console.CancellationToken))
                    {
                        if (message.KnownFormat == StressLogKnownFormat.TaskSwitch)
                        {
                            writer.WriteLine($"Task was switched from {message.GetArgument(0):x}");
                        }
                        else
                        {
                            string text = StressLogFormat.FormatMessageText(Runtime, message, pointerHexDigits);
                            if (text.Contains(UnresolvedFormatMarker, StringComparison.Ordinal))
                            {
                                unresolvedCount++;
                            }

                            output.WriteRow(
                                message.OSThreadId.ToString("x"),
                                message.ElapsedSeconds.ToString("F9"),
                                StressLogFormat.FacilityName(message.Facility),
                                text);
                        }

                        messageCount++;
                    }

                    if (Console.CancellationToken.IsCancellationRequested)
                    {
                        writer.WriteLine("----- Interrupted by user -----");
                        interrupted = true;
                    }

                    writer.WriteLine($"---------------------------- {messageCount} total entries ------------------------------------");
                }

                Console.WriteLine(interrupted ? "Stress log dump interrupted by user" : "SUCCESS: Stress log dumped");

                if (!interrupted)
                {
                    WarnIfFormatsUnresolved(messageCount, unresolvedCount);
                }
            }
            catch (IOException ex)
            {
                WriteLineError($"Failed to write stress log to '{fileName}': {ex.Message}");
            }
            finally
            {
                if (ownsStressLog)
                {
                    stressLog.Dispose();
                }
            }
        }

        private static void CountThreadsAndElapsed(StressLog stressLog, CancellationToken cancellationToken, out int threadCount, out double elapsedSeconds)
        {
            HashSet<ulong> threads = new();
            double elapsed = 0;
            foreach (StressLogMessage message in stressLog.EnumerateMessages(cancellationToken))
            {
                threads.Add(message.OSThreadId);
                if (message.ElapsedSeconds > elapsed)
                {
                    elapsed = message.ElapsedSeconds;
                }
            }

            threadCount = threads.Count;
            elapsedSeconds = elapsed;
        }

        private void WarnIfFormatsUnresolved(int messageCount, int unresolvedCount)
        {
            if (messageCount <= 0 || unresolvedCount <= 0)
            {
                return;
            }

            int percent = (int)((long)unresolvedCount * 100 / messageCount);
            if (percent < UnresolvedWarningThresholdPercent)
            {
                return;
            }

            string runtimeModule = GetRuntimeModuleFileName();
            WriteLineWarning(
                $"WARNING: {unresolvedCount} of {messageCount} messages ({percent}%) could not resolve their format strings.");
            WriteLineWarning(
                "The format strings live in the .NET runtime's read-only data, which this dump does not contain.");
            WriteLineWarning(
                "Set the symbol path to the matching runtime and re-run dumplog, for example:");
            WriteLineWarning(
                $"    setsymbolserver -directory <dir>   (where <dir> contains {runtimeModule})");
        }

        private string GetRuntimeModuleFileName()
        {
            try
            {
                string fileName = Runtime?.ClrInfo?.ModuleInfo?.FileName;
                if (!string.IsNullOrEmpty(fileName))
                {
                    return Path.GetFileName(fileName);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
            }

            return "the .NET runtime (libcoreclr.so or coreclr.dll)";
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
DumpLog [-addr <addressOfStressLog>] [<Filename>]

To aid in diagnosing hard-to-reproduce stress failures, the CLR maintains an
in-memory log that avoids locks or I/O so it does not disturb a fragile repro.
DumpLog writes that log out to a file. If no Filename is specified, the file
'StressLog.txt' in the current directory is created.

The optional -addr argument specifies the hex address of a stress log other than
the runtime's own (for example a utilcode-linked module's log).

To turn on the stress log, set the following environment variables before
starting the .NET app:

    set DOTNET_StressLog=1
    set DOTNET_LogFacility=0xffffffff   (bitmask of facilities to log)
    set DOTNET_StressLogSize=0x10000    (per-thread log size in bytes)
    set DOTNET_LogLevel=10              (higher means more detail, max 10)

The stress log is circular, so new entries replace older ones once a thread
reaches its buffer limit.
";
    }
}
