// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;

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

        public static TraceFileFormat DefaultTraceFileFormat => TraceFileFormat.NetTrace;

        public static Option FormatOption() =>
            new Option(
                aliases: new[] { "--format" },
                description: $"Sets the output format for the trace file.  Default is {DefaultTraceFileFormat}",
                argument: new Argument<TraceFileFormat>(defaultValue: DefaultTraceFileFormat) { Name = "trace-file-format" },
                isHidden: false);

        public static Option ConvertFormatOption() =>
            new Option(
                aliases: new[] { "--format" },
                description: $"Sets the output format for the trace file conversion.",
                argument: new Argument<TraceFileFormat> { Name = "trace-file-format" },
                isHidden: false);

        private static uint DefaultCircularBufferSizeInMB => 256;

        public static Option CircularBufferOption() =>
            new Option(
                alias: "--buffersize",
                description: $"Sets the size of the in-memory circular buffer in megabytes. Default {DefaultCircularBufferSizeInMB} MB.",
                argument: new Argument<uint>(defaultValue: DefaultCircularBufferSizeInMB) { Name = "size" },
                isHidden: false);

        public static Option ProvidersOption() =>
            new Option(
                alias: "--providers",
                description: @"A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: 'KnownProviderName[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'. These providers are in addition to any providers implied by the --profile argument. If there is any discrepancy for a particular provider, the configuration here takes precedence over the implicit configuration from the profile.",
                argument: new Argument<string>(defaultValue: "") { Name = "list-of-comma-separated-providers" }, // TODO: Can we specify an actual type?
                isHidden: false);

        public static Option ProfileOption() =>
            new Option(
                alias: "--profile",
                description: @"A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.",
                argument: new Argument<string>(defaultValue: "") { Name = "profile-name" },
                isHidden: false);

        public static Option DurationOption() =>
            new Option(
                alias: "--duration",
                description: @"When specified, will trace for the given timespan and then automatically stop the trace. Provided in the form of dd:hh:mm:ss.",
                argument: new Argument<TimeSpan>(defaultValue: default(TimeSpan)) { Name = "duration-timespan" },
                isHidden: true);
    }
}
