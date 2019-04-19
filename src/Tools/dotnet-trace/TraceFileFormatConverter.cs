// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.Stacks.Formats;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal enum TraceFileFormat { netperf, speedscope };
    internal static class TraceFileFormatConverter
    {
        public static void ConvertToFormat(TraceFileFormat format, string fileToConvert)
        {
            // TODO convert to appropriate file format
            switch (format)
            {
                case TraceFileFormat.netperf:
                    break;
                case TraceFileFormat.speedscope:
                    Console.Out.WriteLine($"Converting to {format}...");
                    ConvertToSpeedscope(fileToConvert);
                    break;
                default:
                    // Validation happened way before this, so we shoud never reach this...
                    throw new System.Exception($"Invalid TraceFileFormat \"{format}\"");
            }
        }

        private static void ConvertToSpeedscope(string fileToConvert)
        {
            var symbolReader = new SymbolReader(System.IO.TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath };
            var etlxFilePath = TraceLog.CreateFromEventPipeDataFile(fileToConvert);

            var eventLog = new TraceLog(etlxFilePath);

            try
            {
                var stackSource = new MutableTraceEventStackSource(eventLog)
                {
                    OnlyManagedCodeStacks = true // EventPipe currently only has managed code stacks.
                };

                var computer = new SampleProfilerThreadTimeComputer(eventLog, symbolReader);
                computer.GenerateThreadTimeStacks(stackSource); 

                var speedScopeFilePath = Path.ChangeExtension(fileToConvert, "speedscope.json");

                SpeedScopeStackSourceWriter.WriteStackViewAsJson(stackSource, speedScopeFilePath);
            }
            finally
            {
                eventLog.Dispose();

                if (File.Exists(etlxFilePath))
                {
                    File.Delete(etlxFilePath);
                }
            }
        }
    }
}