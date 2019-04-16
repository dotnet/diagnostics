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
                argument: new Argument<int> { Name = "pid" });

        public static Option OutputPathOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: "The output path for the collected trace data. If not specified it defaults to 'trace.netperf'",
                argument: new Argument<string>(defaultValue: $"trace.netperf") { Name = "trace-file-path" });

        public static Option ProvidersOption() =>
            new Option(
                alias: "--providers",
                description: @"A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: '(GUID|KnownProviderName)[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'",
                argument: new Argument<string> { Name = "list-of-comma-separated-providers" }); // TODO: Can we specify an actual type?

        // This is a hidden option, currently not in the design-doc spec.
        private static uint DefaultCircularBufferSizeInMB => 64;
        public static Option CircularBufferOption() =>
            new Option(
                alias: "--buffersize",
                description: $"Sets the size of the in-memory circular buffer in megabytes. Default {DefaultCircularBufferSizeInMB} MB",
                argument: new Argument<uint>(defaultValue: DefaultCircularBufferSizeInMB) { Name = "size" },
                isHidden: true);
    }
}
