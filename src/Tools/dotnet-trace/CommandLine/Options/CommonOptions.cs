// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CommonOptions
    {
        public static readonly Option<int> ProcessIdOption =
            new("--process-id", "-p")
            {
                Description = "The process id to collect the trace."
            };

        public static readonly Option<string> NameOption =
            new("--name", "-n")
            {
                Description = "The name of the process to collect the trace.",
            };

        public static TraceFileFormat DefaultTraceFileFormat() => TraceFileFormat.NetTrace;

        public static readonly Option<TraceFileFormat> FormatOption =
            new("--format")
            {
                Description =  $"If not using the default NetTrace format, an additional file will be emitted with the specified format under the same output name and with the corresponding format extension. The default format is {DefaultTraceFileFormat()}.",
                DefaultValueFactory = _ => DefaultTraceFileFormat()
            };

        public static readonly Option<TraceFileFormat> ConvertFormatOption =
            new("--format")
            {
                Description = $"Sets the output format for the trace file conversion.",
                Required = true
            };
    }
}
