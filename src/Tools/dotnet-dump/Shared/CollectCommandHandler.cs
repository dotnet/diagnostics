// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;

namespace Microsoft.Diagnostics.Tools.Dump
{
    /// <summary>
    /// Shared command handler for dump collection functionality.
    /// This allows both dotnet-dump and dotnet-collect-dump to share the same collect command logic.
    /// </summary>
    public static class CollectCommandHandler
    {
        public static readonly Option<int> ProcessIdOption =
            new("--process-id", "-p")
            {
                Description = "The process id to collect a memory dump."
            };

        public static readonly Option<string> ProcessNameOption =
            new("--name", "-n")
            {
                Description = "The name of the process to collect a memory dump."
            };

        public static readonly Option<string> OutputOption =
            new("--output", "-o")
            {
                Description = @"The path where collected dumps should be written. Defaults to '.\dump_YYYYMMDD_HHMMSS.dmp' on Windows and './core_YYYYMMDD_HHMMSS'
on Linux where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path and file name of the dump."
            };

        public static readonly Option<bool> DiagnosticLoggingOption =
            new("--diag")
            {
                Description = "Enable dump collection diagnostic logging."
            };

        public static readonly Option<bool> CrashReportOption =
            new("--crashreport")
            {
                Description = "Enable crash report generation."
            };

        public static readonly Option<Dumper.DumpTypeOption> TypeOption =
            new("--type")
            {
                Description = @"The dump type determines the kinds of information that are collected from the process. There are several types: Full - The largest dump containing all memory including the module images. Heap - A large and relatively comprehensive dump containing module lists, thread lists, all stacks, exception information, handle information, and all memory except for mapped images. Mini - A small dump containing module lists, thread lists, exception information and all stacks. Triage - A small dump containing module lists, thread lists, exception information, all stacks and PII removed.",
                DefaultValueFactory = _ => Dumper.DumpTypeOption.Full
            };

        public static readonly Option<string> DiagnosticPortOption =
            new("--diagnostic-port", "--dport")
            {
                Description = "The path to a diagnostic port to be used. Must be a runtime connect port."
            };

        /// <summary>
        /// Creates the collect command with all necessary options and action handler.
        /// </summary>
        /// <returns>The configured collect command.</returns>
        public static Command CollectCommand()
        {
            Command command = new(name: "collect", description: "Capture dumps from a process")
            {
                ProcessIdOption,
                OutputOption,
                DiagnosticLoggingOption,
                CrashReportOption,
                TypeOption,
                ProcessNameOption,
                DiagnosticPortOption
            };

            command.SetAction((parseResult) => new Dumper().Collect(
                stdOutput: parseResult.Configuration.Output,
                stdError: parseResult.Configuration.Error,
                processId: parseResult.GetValue(ProcessIdOption),
                output: parseResult.GetValue(OutputOption),
                diag: parseResult.GetValue(DiagnosticLoggingOption),
                crashreport: parseResult.GetValue(CrashReportOption),
                type: parseResult.GetValue(TypeOption),
                name: parseResult.GetValue(ProcessNameOption),
                diagnosticPort: parseResult.GetValue(DiagnosticPortOption)));

            return command;
        }
    }
}
