// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.Stacks.Formats;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal enum TraceFileFormat { NetTrace = 1, Speedscope, Chromium };

    internal static class TraceFileFormatConverter
    {
        private static readonly IReadOnlyDictionary<TraceFileFormat, string> TraceFileFormatExtensions = new Dictionary<TraceFileFormat, string>() {
            { TraceFileFormat.NetTrace,     "nettrace" },
            { TraceFileFormat.Speedscope,   "speedscope.json" },
            { TraceFileFormat.Chromium,     "chromium.json" }
        };

        internal static string GetConvertedFilename(string fileToConvert, string outputfile, TraceFileFormat format)
        {
            if (string.IsNullOrWhiteSpace(outputfile))
            {
                outputfile = fileToConvert;
            }

            return Path.ChangeExtension(outputfile, TraceFileFormatExtensions[format]);
        }

        internal static void ConvertToFormat(IConsole console, TraceFileFormat format, string fileToConvert, string outputFilename)
        {
            Console.Out.WriteLine($"Writing:\t{outputFilename}");

            switch (format)
            {
                case TraceFileFormat.NetTrace:
                    break;
                case TraceFileFormat.Speedscope:
                case TraceFileFormat.Chromium:
                    try
                    {
                        Convert(console, format, fileToConvert, outputFilename);
                    }
                    // TODO: On a broken/truncated trace, the exception we get from TraceEvent is a plain System.Exception type because it gets caught and rethrown inside TraceEvent.
                    // We should probably modify TraceEvent to throw a better exception.
                    catch (Exception ex)
                    {
                        if (ex.ToString().Contains("Read past end of stream."))
                        {
                            console.Out.WriteLine("Detected a potentially broken trace. Continuing with best-efforts to convert the trace, but resulting speedscope file may contain broken stacks as a result.");
                            Convert(console, format, fileToConvert, outputFilename, continueOnError: true);
                        }
                        else
                        {
                            console.Error.WriteLine(ex.ToString());
                        }
                    }
                    break;
                default:
                    // Validation happened way before this, so we shoud never reach this...
                    throw new ArgumentException($"Invalid TraceFileFormat \"{format}\"");
            }
            console.Out.WriteLine("Conversion complete");
        }

        private static void Convert(IConsole _, TraceFileFormat format, string fileToConvert, string outputFilename, bool continueOnError = false)
        {
            string etlxFilePath = TraceLog.CreateFromEventPipeDataFile(fileToConvert, null, new TraceLogOptions() { ContinueOnError = continueOnError });
            using (SymbolReader symbolReader = new(TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
            using (TraceLog eventLog = new(etlxFilePath))
            {
                MutableTraceEventStackSource stackSource = new(eventLog)
                {
                    OnlyManagedCodeStacks = true // EventPipe currently only has managed code stacks.
                };

                SampleProfilerThreadTimeComputer computer = new(eventLog, symbolReader)
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
