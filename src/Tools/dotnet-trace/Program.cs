// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.Internal.Common.Commands;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class Program
    {
        public static Task<int> Main(string[] args)
        {
            Parser parser = new CommandLineBuilder()
                .AddCommand(CollectCommandHandler.CollectCommand())
                .AddCommand(ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that traces can be collected"))
                .AddCommand(ListProfilesCommandHandler.ListProfilesCommand())
                .AddCommand(ConvertCommandHandler.ConvertCommand())
                .UseDefaults()
                .Build();
            ParseResult parseResult = parser.Parse(args);
            string parsedCommandName = parseResult.CommandResult.Command.Name;
            if (parsedCommandName == "collect")
            {
                IReadOnlyCollection<string> unparsedTokens = parseResult.UnparsedTokens;
                // If we notice there are unparsed tokens, user might want to attach on startup.
                if (unparsedTokens.Count > 0)
                {
                    ProcessLauncher.Launcher.PrepareChildProcess(args);
                }
            }
            return parser.InvokeAsync(args);
        }
    }
}
