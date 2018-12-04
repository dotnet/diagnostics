// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Diagnostics.Tools.Analyze.Commands;

namespace Microsoft.Diagnostics.Tools.Analyze
{
    public class CommandProcessor
    {
        private readonly IList<IAnalysisCommand> _commands;
        private readonly IDictionary<string, IAnalysisCommand> _commandNames;

        public CommandProcessor()
        {
            _commands = GetCommands();
            _commandNames = BuildCommandNamesIndex(_commands);
        }

        public async Task RunAsync(IConsole console, AnalysisSession session, CancellationToken cancellationToken = default)
        {
            await console.Out.WriteLineAsync("Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.");
            await console.Out.WriteLineAsync("Type 'quit' or 'exit' to exit the analysis session.");
            while (!cancellationToken.IsCancellationRequested)
            {
                await console.Out.WriteAsync("> ");
                var line = await console.In.ReadLineAsync();
                cancellationToken.ThrowIfCancellationRequested();

                // Naive arg parsing
                var args = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (string.Equals(args[0], "quit", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "q", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "exit", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowHelpAsync(console, args.AsMemory().Slice(1));
                }
                else if (_commandNames.TryGetValue(args[0], out var command))
                {
                    await command.RunAsync(console, args.Skip(1).ToArray(), session);
                }
                else
                {
                    console.Error.WriteLine($"Unknown command: {args[0]}");
                }
            }
        }

        private async Task ShowHelpAsync(IConsole console, ReadOnlyMemory<string> args)
        {
            if (args.Length == 0)
            {
                foreach (var command in _commands)
                {
                    var line = $"* {command.Names[0]} - {command.Description}";
                    if (command.Names.Count > 1)
                    {
                        line += $" (aliases: {string.Join(", ", command.Names.Skip(1))})";
                    }
                    console.WriteLine(line);
                }
            }
            else
            {
                if (_commandNames.TryGetValue(args.Span[0], out var command))
                {
                    await command.WriteHelpAsync(console);
                }
                else
                {
                    console.WriteLine($"Unknown command: {args.Span[0]}");
                }
            }
        }

        private IDictionary<string, IAnalysisCommand> BuildCommandNamesIndex(IEnumerable<IAnalysisCommand> commands)
        {
            var dict = new Dictionary<string, IAnalysisCommand>(StringComparer.OrdinalIgnoreCase);
            foreach (var command in commands)
            {
                foreach (var name in command.Names)
                {
                    dict[name] = command;
                }
            }
            return dict;
        }

        private static List<IAnalysisCommand> GetCommands()
        {
            return typeof(Program).Assembly
                .GetExportedTypes()
                .Where(t => !t.IsAbstract && typeof(IAnalysisCommand).IsAssignableFrom(t))
                .Select(t => (IAnalysisCommand)Activator.CreateInstance(t))
                .ToList();
        }
    }
}
