// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public interface IAnalysisCommand
    {
        IReadOnlyList<string> Names { get; }
        string Description { get; }

        Task RunAsync(IConsole console, string[] args, AnalysisSession session);
        Task WriteHelpAsync(IConsole console);
    }
}
