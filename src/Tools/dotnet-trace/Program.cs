// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.Internal.Common;
using Microsoft.Internal.Common.Commands;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class Program
    {
        public static Task<int> Main(string[] args)
        {
            RootCommand rootCommand = new()
            {
                CollectCommandHandler.CollectCommand(),
                ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that traces can be collected from."),
                ListProfilesCommandHandler.ListProfilesCommand(),
                ConvertCommandHandler.ConvertCommand(),
                ReportCommandHandler.ReportCommand()
            };

            CommandLineConfiguration configuration = new(rootCommand)
            {
                // System.CommandLine should not interfere with Ctrl+C
                ProcessTerminationTimeout = null
            };

            ParseResult parseResult = rootCommand.Parse(args, configuration);
            string parsedCommandName = parseResult.CommandResult.Command.Name;
            if (parsedCommandName == "collect")
            {
                IReadOnlyCollection<string> unparsedTokens = parseResult.UnmatchedTokens;
                // If we notice there are unparsed tokens, user might want to attach on startup.
                if (unparsedTokens.Count > 0)
                {
                    ProcessLauncher.Launcher.PrepareChildProcess(args);
                }
            }
            return parseResult.InvokeAsync();
        }
    }
}
