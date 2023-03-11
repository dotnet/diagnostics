// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CommonOptions
    {
        public static Option ProcessIdOption() =>
            new(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect the trace.")
            {
                Argument = new Argument<int>(name: "pid")
            };

        public static Option NameOption() =>
            new(
                aliases: new[] { "-n", "--name" },
                description: "The name of the process to collect the trace.")
            {
                Argument = new Argument<string>(name: "name")
            };

        public static TraceFileFormat DefaultTraceFileFormat() => TraceFileFormat.NetTrace;

        public static Option FormatOption() =>
            new(
                alias: "--format",
                description: $"Sets the output format for the trace file.  Default is {DefaultTraceFileFormat()}.")
            {
                Argument = new Argument<TraceFileFormat>(name: "trace-file-format", getDefaultValue: DefaultTraceFileFormat)
            };

        public static Option ConvertFormatOption() =>
            new(
                alias: "--format",
                description: $"Sets the output format for the trace file conversion.")
            {
                Argument = new Argument<TraceFileFormat>(name: "trace-file-format")
            };
    }
}
