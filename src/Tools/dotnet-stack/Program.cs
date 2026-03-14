// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Internal.Common;
using Microsoft.Internal.Common.Commands;

namespace Microsoft.Diagnostics.Tools.Stack
{
    internal static class Program
    {
        public static Task<int> Main(string[] args)
        {
            RootCommand rootCommand = new()
            {
                ReportCommandHandler.ReportCommand(),
                ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that stack traces can be collected from."),
                SymbolicateHandler.SymbolicateCommand()
            };

            return rootCommand.Parse(args).InvokeAsync();
        }
    }
}
