// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Internal.Common.Commands;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Dump
{
    class Program
    {
        public static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .AddCommand(CollectCommand())
                .AddCommand(AnalyzeCommand())
                .AddCommand(ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that dumps can be collected from."))
                .UseDefaults()
                .Build();

            return parser.InvokeAsync(args);
        }

        private static Command CollectCommand() =>
            new Command( name: "collect", description: "Capture dumps from a process")
            {
                // Handler
                CommandHandler.Create<IConsole, int, string, bool, bool, Dumper.DumpTypeOption, string>(new Dumper().Collect),
                // Options
                ProcessIdOption(), OutputOption(), DiagnosticLoggingOption(), CrashReportOption(), TypeOption(), ProcessNameOption()
            };

        private static Option ProcessIdOption() =>
            new Option(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect a memory dump.")
            {
                Argument = new Argument<int>(name: "pid")
            };

        private static Option ProcessNameOption() =>
            new Option(
                aliases: new[] { "-n", "--name" },
                description: "The name of the process to collect a memory dump.")
            {
                Argument = new Argument<string>(name: "name")
            };

        private static Option OutputOption() =>
            new Option( 
                aliases: new[] { "-o", "--output" },
                description: @"The path where collected dumps should be written. Defaults to '.\dump_YYYYMMDD_HHMMSS.dmp' on Windows and './core_YYYYMMDD_HHMMSS' 
on Linux where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path and file name of the dump.") 
            {
                Argument = new Argument<string>(name: "output_dump_path")
            };

        private static Option DiagnosticLoggingOption() =>
            new Option(
                alias: "--diag", 
                description: "Enable dump collection diagnostic logging.") 
            {
                Argument = new Argument<bool>(name: "diag")
            };

        private static Option CrashReportOption() =>
            new Option(
                alias: "--crashreport", 
                description: "Enable crash report generation.") 
            {
                Argument = new Argument<bool>(name: "crashreport")
            };

        private static Option TypeOption() =>
            new Option(
                alias: "--type",
                description: @"The dump type determines the kinds of information that are collected from the process. There are several types: Full - The largest dump containing all memory including the module images. Heap - A large and relatively comprehensive dump containing module lists, thread lists, all stacks, exception information, handle information, and all memory except for mapped images. Mini - A small dump containing module lists, thread lists, exception information and all stacks. Triage - A small dump containing module lists, thread lists, exception information, all stacks and PII removed.")
            {
                Argument = new Argument<Dumper.DumpTypeOption>(name: "dump_type", getDefaultValue: () => Dumper.DumpTypeOption.Full)
            };

        private static Command AnalyzeCommand() =>
            new Command(
                name: "analyze", 
                description: "Starts an interactive shell with debugging commands to explore a dump")
            {
                // Handler
                CommandHandler.Create<FileInfo, string[]>(new Analyzer().Analyze),
                // Arguments and Options
                DumpPath(),
                RunCommand() 
            };

        private static Argument DumpPath() =>
            new Argument<FileInfo>(
                name: "dump_path")
            {
                Description = "Name of the dump file to analyze."
            }.ExistingOnly();

        private static Option RunCommand() =>
            new Option(
                aliases: new[] { "-c", "--command" }, 
                description: "Runs the command on start. Multiple instances of this parameter can be used in an invocation to chain commands. Commands will get run in the order that they are provided on the command line. If you want dotnet dump to exit after the commands, your last command should be 'exit'.") 
            {
                Argument = new Argument<string[]>(name: "command", getDefaultValue: () => Array.Empty<string>()) { Arity = ArgumentArity.ZeroOrMore }
            };
    }
}
