// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Internal.Common;
using Microsoft.Internal.Common.Commands;

namespace Microsoft.Diagnostics.Tools.Dump
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            RootCommand rootCommand = new()
            {
                CollectCommandHandler.CollectCommand(),
                AnalyzeCommand(),
                ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that dumps can be collected from.")
            };

            return rootCommand.Parse(args).Invoke();
        }

        private static Command AnalyzeCommand()
        {
            Command command = new(
                name: "analyze",
                description: "Starts an interactive shell with debugging commands to explore a dump")
            {
                DumpPath,
                RunCommand
            };

            command.SetAction((parseResult) => new Analyzer().Analyze(
                parseResult.GetValue(DumpPath),
                parseResult.GetValue(RunCommand) ?? Array.Empty<string>()));

            return command;
        }

        private static readonly Argument<FileInfo> DumpPath =
            new Argument<FileInfo>(name: "dump_path")
            {
                Description = "Name of the dump file to analyze."
            }.AcceptExistingOnly();

        private static readonly Option<string[]> RunCommand =
            new("--command", "-c")
            {
                Description = "Runs the command on start. Multiple instances of this parameter can be used in an invocation to chain commands. Commands will get run in the order that they are provided on the command line. If you want dotnet dump to exit after the commands, your last command should be 'exit'.",
                Arity = ArgumentArity.ZeroOrMore
            };
    }
}
