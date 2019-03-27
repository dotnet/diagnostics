// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
    }
}
