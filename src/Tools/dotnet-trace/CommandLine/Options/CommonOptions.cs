// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CommonOptions
    {
        public static Option ProcessIdOption() =>
            new Option(
                aliases: new[] { "-p", "--process-id" },
                description: "The process to collect the trace from",
                argument: new Argument<int> { Name = "pid" },
                isHidden: false);

        public static TraceFileFormat DefaultTraceFileFormat => TraceFileFormat.Netperf;

        public static Option FormatOption() =>
            new Option(
                aliases: new[] { "--format" },
                description: $"Sets the output format for the trace file.  Default is {DefaultTraceFileFormat}",
                argument: new Argument<TraceFileFormat>(defaultValue: DefaultTraceFileFormat) { Name = "trace-file-format" },
                isHidden: false);
    }
}
