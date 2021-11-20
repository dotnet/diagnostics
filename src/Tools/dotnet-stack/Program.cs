// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.Internal.Common.Commands;

namespace Microsoft.Diagnostics.Tools.Stack
{
    internal static class Program
    {
        public static Task<int> Main(string[] args)
        {
            Parser parser = new CommandLineBuilder()
                .AddCommand(ReportCommandHandler.ReportCommand())
                .AddCommand(ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that traces can be collected"))
                .AddCommand(SymbolicateHandler.SymbolicateCommand())
                .UseDefaults()
                .Build();

            return parser.InvokeAsync(args);
        }
    }
}
