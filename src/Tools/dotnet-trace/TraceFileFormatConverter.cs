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
    internal enum TraceFileFormat { Netperf, Speedscope };

    internal static class TraceFileFormatConverter
    {
        private static Dictionary<TraceFileFormat, string> TraceFileFormatExtensions = new Dictionary<TraceFileFormat, string>() {
            { TraceFileFormat.Netperf,      "netperf" },
            { TraceFileFormat.Speedscope,   "speedscope.json" }
        };

        public static void ConvertToFormat(TraceFileFormat format, string fileToConvert, string outputFilename = "")
        {
            if (string.IsNullOrWhiteSpace(outputFilename))
                outputFilename = fileToConvert;

            outputFilename = Path.ChangeExtension(outputFilename, TraceFileFormatExtensions[format]);
            Console.Out.WriteLine($"Writing:\t{outputFilename}");

            switch (format)
            {
                case TraceFileFormat.Netperf:
                    break;
                case TraceFileFormat.Speedscope:
                    ConvertToSpeedscope(fileToConvert, outputFilename);
                    break;
                default:
                    // Validation happened way before this, so we shoud never reach this...
                    throw new ArgumentException($"Invalid TraceFileFormat \"{format}\"");
            }
            Console.Out.WriteLine("Conversion complete");
        }

        private static void ConvertToSpeedscope(string fileToConvert, string outputFilename)
        {
            var etlxFilePath = TraceLog.CreateFromEventPipeDataFile(fileToConvert);
            using (var symbolReader = new SymbolReader(System.IO.TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
            using (var eventLog = new TraceLog(etlxFilePath))
            {
                var stackSource = new MutableTraceEventStackSource(eventLog)
                {
                    OnlyManagedCodeStacks = true // EventPipe currently only has managed code stacks.
                };

                var computer = new SampleProfilerThreadTimeComputer(eventLog, symbolReader);
                computer.GenerateThreadTimeStacks(stackSource);

                SpeedScopeStackSourceWriter.WriteStackViewAsJson(stackSource, outputFilename);
            }

            if (File.Exists(etlxFilePath))
            {
                File.Delete(etlxFilePath);
            }
            
        }
    }
}
