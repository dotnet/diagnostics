// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Common.Commands;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Dump
{
    // Make sure the name of the fields match the option names.
    internal record struct DumpCollectionConfig(
            int ProcessId,
            string ProcessName,
            string DiagnosticPort,
            string DumpOutputPath,
            bool EnableDiagnosticOutput,
            bool GenerateCrashReport,
            Dumper.DumpTypeOption DumpType);

    internal static class Program
    {
        public static Task<int> Main(string[] args)
        {
            Parser parser = new CommandLineBuilder()
                .AddCommand(CollectCommand())
                .AddCommand(AnalyzeCommand())
                .AddCommand(ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that dumps can be collected from."))
                .UseDefaults()
                .Build();

            return parser.InvokeAsync(args);
        }

        private static Command CollectCommand() =>
            new(name: "collect", description: "Capture dumps from a process")
            {
                // Handler
                CommandHandler.Create<DumpCollectionConfig, IConsole>(Dumper.Collect),
                // Options
                ProcessIdOption(), ProcessNameOption(), DiagnosticPortOption(), OutputOption(), DiagnosticLoggingOption(), CrashReportOption(), TypeOption()
            };

        private static Option ProcessIdOption() =>
            new(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect a memory dump.")
            {
                Name = nameof(DumpCollectionConfig.ProcessId),
                Argument = new Argument<int>(name: "pid")
            };

        private static Option ProcessNameOption() =>
            new(
                aliases: new[] { "-n", "--name" },
                description: "The name of the process to collect a memory dump.")
            {
                Name = nameof(DumpCollectionConfig.ProcessName),
                Argument = new Argument<string>(name: "ProcessName")
            };

        private static Option DiagnosticPortOption() =>
            new(
                alias: "--diagnostic-port",
                description: @"The path to a diagnostic port to be used.")
            {
                Name = nameof(DumpCollectionConfig.DiagnosticPort),
                Argument = new Argument<string>(name: "diagnosticPort")
            };

        private static Option OutputOption() =>
            new(
                aliases: new[] { "-o", "--output" },
                description: @"The path where collected dumps should be written. Defaults to '.\dump_YYYYMMDD_HHMMSS.dmp' on Windows and './core_YYYYMMDD_HHMMSS'
on Linux where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path and file name of the dump.")
            {
                Name = nameof(DumpCollectionConfig.DumpOutputPath),
                Argument = new Argument<string>(name: "output_dump_path")
            };

        private static Option DiagnosticLoggingOption() =>
            new(
                alias: "--diag",
                description: "Enable dump collection diagnostic logging.")
            {
                Name = nameof(DumpCollectionConfig.EnableDiagnosticOutput),
                Argument = new Argument<bool>(name: "diag")
            };

        private static Option CrashReportOption() =>
            new(
                alias: "--crashreport",
                description: "Enable crash report generation.")
            {
                Name = nameof(DumpCollectionConfig.GenerateCrashReport),
                Argument = new Argument<bool>(name: "crashreport")
            };

        private static Option TypeOption() =>
            new(
                alias: "--type",
                description: @"The dump type determines the kinds of information that are collected from the process. There are several types: Full - The largest dump containing all memory including the module images. Heap - A large and relatively comprehensive dump containing module lists, thread lists, all stacks, exception information, handle information, and all memory except for mapped images. Mini - A small dump containing module lists, thread lists, exception information and all stacks. Triage - A small dump containing module lists, thread lists, exception information, all stacks and PII removed.")
            {
                Name = nameof(DumpCollectionConfig.DumpType),
                Argument = new Argument<Dumper.DumpTypeOption>(name: "dump_type", getDefaultValue: () => Dumper.DumpTypeOption.Full)
            };

        private static Command AnalyzeCommand() =>
            new(
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
            new(
                aliases: new[] { "-c", "--command" },
                description: "Runs the command on start. Multiple instances of this parameter can be used in an invocation to chain commands. Commands will get run in the order that they are provided on the command line. If you want dotnet dump to exit after the commands, your last command should be 'exit'.")
            {
                Argument = new Argument<string[]>(name: "command", getDefaultValue: () => Array.Empty<string>()) { Arity = ArgumentArity.ZeroOrMore }
            };
    }
}
