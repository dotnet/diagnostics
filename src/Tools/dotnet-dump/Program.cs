// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Commands;
using Microsoft.Tools.Common;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Dump
{
    class Program
    {
        public static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .AddCommand(CollectCommand())
                .AddCommand(AnalyzeCommand())
                .AddCommand(ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that dumps can be collected"))
                .UseDefaults()
                .Build();

            return parser.InvokeAsync(args);
        }

        private static Command CollectCommand() =>
            new Command( name: "collect", description: "Capture dumps from a process")
            {
                // Handler
                CommandHandler.Create<IConsole, int, string, string, bool, Dumper.DumpTypeOption>(new Dumper().Collect),
                // Options
                ProcessIdOption(), TransportPathOption(), OutputOption(), DiagnosticLoggingOption(), TypeOption()
            };

        private static Option ProcessIdOption() =>
            new Option(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect a memory dump.")
            {
                Argument = new Argument<int>(name: "pid")
            };

        private static Option TransportPathOption() =>
            new Option(
                alias: "--transport-path",
                description: "A fully qualified path and filename for the OS transport to communicate over.  Supersedes the pid argument if provided.")
            {
                Argument = new Argument<string>(name: "transportPath")
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

        private static Option TypeOption() =>
            new Option(
                alias: "--type",
                description: @"The dump type determines the kinds of information that are collected from the process. There are two types: heap - A large and 
relatively comprehensive dump containing module lists, thread lists, all stacks, exception information, handle information, and all memory except for mapped 
images. mini - A small dump containing module lists, thread lists, exception information and all stacks. If not specified 'heap' is the default.")
            {
                Argument = new Argument<Dumper.DumpTypeOption>(name: "dump_type", defaultValue: Dumper.DumpTypeOption.Heap)
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
                description: "Run the command on start.") 
            {
                Argument = new Argument<string[]>(name: "command", defaultValue: new string[0]) { Arity = ArgumentArity.ZeroOrMore }
            };
    }
}
