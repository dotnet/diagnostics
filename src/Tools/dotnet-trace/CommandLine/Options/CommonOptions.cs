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
                new[] { "-pid" },
                "The unique identifier of the associated process to connect to.",
                new Argument<int> { Name = "ProcessId" });

        public static Option OutputPathOption() =>
            new Option(
                new[] { "-o", "--output" },
                @"The .netperf file name to log events to.",
                new Argument<string>(defaultValue: $"eventpipe-{DateTime.Now:yyyyMMdd_HHmmss}.netperf") {
                    Name = "FILE_NAME",
                });

        public static Option CircularBufferOption() =>
            new Option(
                new[] { "--buffersize" },
                @"Sets the size of the in-memory circular buffer in megabytes.",
                new Argument<uint>(defaultValue: 64) { Name = "SIZE" });

        public static Option ProvidersOption() =>
            new Option(
                aliases: new[] { "--providers" },
                description: @"A list EventPipe provider to be enabled in the form 'Provider[,Provider]', where Provider is in the form: '(GUID|KnownProviderName)[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'",
                argument: new Argument<string> { Name = "PROVIDERS" }); // TODO: Can we specify an actual type?
    }
}
