// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.IO;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CommonOptions
    {
        public static readonly Option<string> ProvidersOption =
            new("--providers")
            {
                Description = @"A comma delimited list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]'," +
                             @"where Provider is in the form: 'KnownProviderName[:[Flags][:[Level][:[KeyValueArgs]]]]', and KeyValueArgs is in the form: " +
                             @"'[key1=value1][;key2=value2]'.  Values in KeyValueArgs that contain ';' or '=' characters need to be surrounded by '""', " +
                             @"e.g., FilterAndPayloadSpecs=""MyProvider/MyEvent:-Prop1=Prop1;Prop2=Prop2.A.B;"".  Depending on your shell, you may need to " +
                             @"escape the '""' characters and/or surround the entire provider specification in quotes, e.g., " +
                             @"--providers 'KnownProviderName:0x1:1:FilterSpec=\""KnownProviderName/EventName:-Prop1=Prop1;Prop2=Prop2.A.B;\""'. These providers are in " +
                             @"addition to any providers implied by the --profile argument. If there is any discrepancy for a particular provider, the " +
                             @"configuration here takes precedence over the implicit configuration from the profile.  See documentation for examples."
            };

        public static readonly Option<string> CLREventLevelOption =
            new("--clreventlevel")
            {
                Description = @"Verbosity of CLR events to be emitted."
            };

        public static readonly Option<string> CLREventsOption =
            new("--clrevents")
            {
                Description = @"List of CLR runtime events to emit."
            };

        public static readonly Option<string> ProfileOption =
            new("--profile")
            {
                Description = @"A named, pre-defined set of provider configurations for common tracing scenarios. You can specify multiple profiles as a comma-separated list. When multiple profiles are specified, the providers and settings are combined (union), and duplicates are ignored."
            };

        public static TraceFileFormat DefaultTraceFileFormat() => TraceFileFormat.NetTrace;

        public static readonly Option<TraceFileFormat> FormatOption =
            new("--format")
            {
                Description =  $"If not using the default NetTrace format, an additional file will be emitted with the specified format under the same output name and with the corresponding format extension. The default format is {DefaultTraceFileFormat()}.",
                DefaultValueFactory = _ => DefaultTraceFileFormat()
            };

        public static string DefaultTraceName => "default";

        public static readonly Option<FileInfo> OutputPathOption =
            new("--output", "-o")
            {
                Description = $"The output path for the collected trace data. If not specified it defaults to '<appname>_<yyyyMMdd>_<HHmmss>.nettrace', e.g., 'myapp_20210315_111514.nettrace'.",
                DefaultValueFactory = _ => new FileInfo(DefaultTraceName)
            };

        public static readonly Option<TimeSpan> DurationOption =
            new("--duration")
            {
                Description = @"When specified, will trace for the given timespan and then automatically stop the trace. Provided in the form of dd:hh:mm:ss."
            };

        public static readonly Option<string> NameOption =
            new("--name", "-n")
            {
                Description = "The name of the process to collect the trace.",
            };

        public static readonly Option<int> ProcessIdOption =
            new("--process-id", "-p")
            {
                Description = "The process id to collect the trace."
            };

        public static readonly Option<TraceFileFormat> ConvertFormatOption =
            new("--format")
            {
                Description = $"Sets the output format for the trace file conversion.",
                Required = true
            };
    }
}
