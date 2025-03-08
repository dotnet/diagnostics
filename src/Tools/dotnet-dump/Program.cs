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
        public static Task<int> Main(string[] args)
        {
            RootCommand rootCommand = new()
            {
                CollectCommand(),
                AnalyzeCommand(),
                ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that dumps can be collected from.")
            };

            return rootCommand.Parse(args).InvokeAsync();
        }

        private static Command CollectCommand()
        {
            Command command = new(name: "collect", description: "Capture dumps from a process")
            {
                ProcessIdOption, OutputOption, DiagnosticLoggingOption, CrashReportOption, TypeOption, ProcessNameOption, DiagnosticPortOption
            };

            command.SetAction((parseResult, ct) => Task.FromResult(new Dumper().Collect(
                stdOutput: parseResult.Configuration.Output,
                stdError: parseResult.Configuration.Error,
                processId: parseResult.GetValue(ProcessIdOption),
                output: parseResult.GetValue(OutputOption),
                diag: parseResult.GetValue(DiagnosticLoggingOption),
                crashreport: parseResult.GetValue(CrashReportOption),
                type: parseResult.GetValue(TypeOption),
                name: parseResult.GetValue(ProcessNameOption),
                diagnosticPort: parseResult.GetValue(DiagnosticPortOption))));

            return command;
        }

        private static readonly Option<int> ProcessIdOption =
            new("--process-id", "-p")
            {
                Description = "The process id to collect a memory dump."
            };

        private static readonly Option<string> ProcessNameOption =
            new("--name", "-n")
            {
                Description = "The name of the process to collect a memory dump."
            };

        private static readonly Option<string> OutputOption =
            new("--output", "-o")
            {
                Description = @"The path where collected dumps should be written. Defaults to '.\dump_YYYYMMDD_HHMMSS.dmp' on Windows and './core_YYYYMMDD_HHMMSS' 
on Linux where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path and file name of the dump."
            };

        private static readonly Option<bool> DiagnosticLoggingOption =
            new("--diag")
            {
                Description = "Enable dump collection diagnostic logging."
            };

        private static readonly Option<bool> CrashReportOption =
            new("--crashreport")
            {
                Description = "Enable crash report generation."
            };

        private static readonly Option<Dumper.DumpTypeOption> TypeOption =
            new("--type")
            {
                Description = @"The dump type determines the kinds of information that are collected from the process. There are several types: Full - The largest dump containing all memory including the module images. Heap - A large and relatively comprehensive dump containing module lists, thread lists, all stacks, exception information, handle information, and all memory except for mapped images. Mini - A small dump containing module lists, thread lists, exception information and all stacks. Triage - A small dump containing module lists, thread lists, exception information, all stacks and PII removed.",
                DefaultValueFactory = _ => Dumper.DumpTypeOption.Full
            };

        private static readonly Option<string> DiagnosticPortOption =
            new("--diagnostic-port", "--dport")
            {
                Description = "The path to a diagnostic port to be used. Must be a runtime connect port."
            };

        private static Command AnalyzeCommand()
        {
            Command command = new(
                name: "analyze",
                description: "Starts an interactive shell with debugging commands to explore a dump")
            {
                DumpPath,
                RunCommand
            };

            command.SetAction((parseResult, ct) => new Analyzer().Analyze(
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
