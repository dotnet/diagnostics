// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.CommandLine;

namespace Microsoft.Diagnostics.Tools.Trace.CommandLine.Options
{
    internal static class DiagnosticProfiles
    {
        public static Option GCPauseProfileOption() =>
            new Option(
                alias: "--gc-pause",
                description: "Collect diagnostic trace about gc pauses. For more details, use dotnet-trace help --gc-pause.")
            {
                Argument = new Argument<string>(name: "gcPause", getDefaultValue: () => null)
            };

        public static Option HttpProfileOption() =>
            new Option(
                alias: "--http",
                description: "Collect diagnostic trace about outbound HTTP requests. For more details, use dotnet-trace help --http.")
            {
                Argument = new Argument<string>(name: "http", getDefaultValue: () => null)
            };

        public static Option LoaderBinderProfileOption() =>
            new Option(
                alias: "--loader-binder",
                description: "Collect diagnostic trace about the assembly loader and binder. For more details, use dotnet-trace help --loader-binder.")
            {
                Argument = new Argument<string>(name: "loaderBinder", getDefaultValue: () => null)
            };
    }
}
