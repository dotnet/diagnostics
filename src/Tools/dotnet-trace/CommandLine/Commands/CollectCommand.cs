// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CollectCommandHandler
    {
        delegate Task<int> CollectDelegate(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration);

        /// <summary>
        /// Collects a diagnostic trace from a currently running process.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the trace from.</param>
        /// <param name="output">The output path for the collected trace data.</param>
        /// <param name="buffersize">Sets the size of the in-memory circular buffer in megabytes.</param>
        /// <param name="providers">A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: 'KnownProviderName[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'</param>
        /// <param name="profile">A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.</param>
        /// <param name="format">The desired format of the created trace file.</param>
        /// <returns></returns>
        private static Task<int> Collect(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration)
        {
            return CommandHelpers.Trace(ct, console, processId, buffersize, providers, profile, duration,
                onBeforeStart: () =>
                {
                    Debug.Assert(output != null);
                },
                onStart: (info) =>
                {
                    using (var fs = new FileStream(output.FullName, FileMode.Create, FileAccess.Write))
                    {
                        Console.Out.WriteLine($"Process        : {info.ProcessFileName}");
                        Console.Out.WriteLine($"Output File    : {fs.Name}");
                        if (info.ShouldStopAfterDuration)
                            Console.Out.WriteLine($"Trace Duration : {duration.ToString(@"dd\:hh\:mm\:ss")}");

                        Console.Out.WriteLine("\n\n");
                        var buffer = new byte[16 * 1024];

                        while (true)
                        {
                            int nBytesRead = info.EventPipeStream.Read(buffer, 0, buffer.Length);
                            if (nBytesRead <= 0)
                                break;
                            fs.Write(buffer, 0, nBytesRead);

                            if (info.HasConsole)
                            {
                                lineToClear = Console.CursorTop - 1;
                                ResetCurrentConsoleLine(info.IsVirtualTerminalModeEnabled);
                            }

                            Console.Out.WriteLine($"[{info.Elapsed.ToString(@"dd\:hh\:mm\:ss")}]\tRecording trace {GetSize(fs.Length)}");
                            Console.Out.WriteLine("Press <Enter> or <Ctrl+C> to exit...");
                            Debug.WriteLine($"PACKET: {Convert.ToBase64String(buffer, 0, nBytesRead)} (bytes {nBytesRead})");
                        }
                    }
                },
                onSuccess: () =>
                {
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("Trace completed.");

                    if (format != TraceFileFormat.NetTrace)
                        TraceFileFormatConverter.ConvertToFormat(format, output.FullName);
                });
        }

        private static int prevBufferWidth = 0;
        private static string clearLineString = "";
        private static int lineToClear = 0;

        private static void ResetCurrentConsoleLine(bool isVTerm)
        {
            if (isVTerm)
            {
                // ANSI escape codes:
                //  [2K => clear current line
                //  [{lineToClear};0H => move cursor to column 0 of row `lineToClear`
                Console.Out.Write($"\u001b[2K\u001b[{lineToClear};0H");
            }
            else
            {
                if (prevBufferWidth != Console.BufferWidth)
                {
                    prevBufferWidth = Console.BufferWidth;
                    clearLineString = new string(' ', Console.BufferWidth - 1);
                }
                Console.SetCursorPosition(0, lineToClear);
                Console.Out.Write(clearLineString);
                Console.SetCursorPosition(0, lineToClear);
            }
        }

        private static string GetSize(long length)
        {
            if (length > 1e9)
                return String.Format("{0,-8} (GB)", $"{length / 1e9:0.00##}");
            else if (length > 1e6)
                return String.Format("{0,-8} (MB)", $"{length / 1e6:0.00##}");
            else if (length > 1e3)
                return String.Format("{0,-8} (KB)", $"{length / 1e3:0.00##}");
            else
                return String.Format("{0,-8} (B)", $"{length / 1.0:0.00##}");
        }

        public static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Collects a diagnostic trace from a currently running process",
                symbols: new Option[] {
                    CommonOptions.ProcessIdOption(),
                    CommonOptions.CircularBufferOption(),
                    OutputPathOption(),
                    CommonOptions.ProvidersOption(),
                    CommonOptions.ProfileOption(),
                    CommonOptions.FormatOption(),
                    CommonOptions.DurationOption()
                },
                handler: HandlerDescriptor.FromDelegate((CollectDelegate)Collect).GetCommandHandler());

        public static string DefaultTraceName => "trace.nettrace";

        private static Option OutputPathOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: $"The output path for the collected trace data. If not specified it defaults to '{DefaultTraceName}'",
                argument: new Argument<FileInfo>(defaultValue: new FileInfo(DefaultTraceName)) { Name = "trace-file-path" },
                isHidden: false);
    }
}
