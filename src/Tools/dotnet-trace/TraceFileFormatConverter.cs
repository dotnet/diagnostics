// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.Stacks.Formats;

namespace Microsoft.Diagnostics.Tools.Trace
{
	internal enum TraceFileFormat { NetTrace, Speedscope, Chromium, CollapsedStacks };

    internal static class TraceFileFormatConverter
    {
        private static IReadOnlyDictionary<TraceFileFormat, string> TraceFileFormatExtensions = new Dictionary<TraceFileFormat, string>() {
            { TraceFileFormat.NetTrace,     "nettrace" },
            { TraceFileFormat.Speedscope,   "speedscope.json" },
            { TraceFileFormat.Chromium,     "chromium.json" },
            { TraceFileFormat.CollapsedStacks, "stacks" }
        };

        public static void ConvertToFormat(TraceFileFormat format, string fileToConvert, string outputFilename = "")
        {
            if (string.IsNullOrWhiteSpace(outputFilename))
                outputFilename = fileToConvert;

            outputFilename = Path.ChangeExtension(outputFilename, TraceFileFormatExtensions[format]);
            Console.Out.WriteLine($"Writing:\t{outputFilename}");

            switch (format)
            {
                case TraceFileFormat.NetTrace:
                    break;
                case TraceFileFormat.Speedscope:
                case TraceFileFormat.Chromium:
                case TraceFileFormat.CollapsedStacks:
                    try
                    {
                        Convert(format, fileToConvert, outputFilename);
                    }
                    // TODO: On a broken/truncated trace, the exception we get from TraceEvent is a plain System.Exception type because it gets caught and rethrown inside TraceEvent.
                    // We should probably modify TraceEvent to throw a better exception.
                    catch (Exception ex)
                    {
                        if (ex.ToString().Contains("Read past end of stream."))
                        {
                            Console.WriteLine("Detected a potentially broken trace. Continuing with best-efforts to convert the trace, but resulting speedscope file may contain broken stacks as a result.");
                            Convert(format, fileToConvert, outputFilename, true);
                        }
                        else
                        {
                            Console.WriteLine(ex);
                        }
                    }
                    break;
                default:
                    // Validation happened way before this, so we shoud never reach this...
                    throw new ArgumentException($"Invalid TraceFileFormat \"{format}\"");
            }
            Console.Out.WriteLine("Conversion complete");
        }

        private static void Convert(TraceFileFormat format, string fileToConvert, string outputFilename, bool continueOnError=false)
        {
            var etlxFilePath = TraceLog.CreateFromEventPipeDataFile(fileToConvert, null, new TraceLogOptions() { ContinueOnError = continueOnError } );
            using (var symbolReader = new SymbolReader(TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
            using (var eventLog = new TraceLog(etlxFilePath))
            {
                var stackSource = new MutableTraceEventStackSource(eventLog)
                {
                    OnlyManagedCodeStacks = true // EventPipe currently only has managed code stacks.
                };

                var computer = new SampleProfilerThreadTimeComputer(eventLog, symbolReader)
                {
                    IncludeEventSourceEvents = false // SpeedScope handles only CPU samples, events are not supported
                };
                computer.GenerateThreadTimeStacks(stackSource);

                switch (format)
                {
                    case TraceFileFormat.Speedscope:
                        SpeedScopeStackSourceWriter.WriteStackViewAsJson(stackSource, outputFilename);
                        break;
                    case TraceFileFormat.Chromium:
                        ChromiumStackSourceWriter.WriteStackViewAsJson(stackSource, outputFilename, compress: false);
                        break;
                    case TraceFileFormat.CollapsedStacks:
                        CollapsedStacksSourceWriter.Write(stackSource, outputFilename);
                        break;
                    default:
                        // we should never get here
                        throw new ArgumentException($"Invalid TraceFileFormat \"{format}\"");
                }
            }

            if (File.Exists(etlxFilePath))
            {
                File.Delete(etlxFilePath);
            }
        }
    }
}
