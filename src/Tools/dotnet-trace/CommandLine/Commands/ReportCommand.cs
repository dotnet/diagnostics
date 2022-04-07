using Microsoft.Tools.Common;
using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace 
{
    internal static class ReportCommandHandler 
    {
        delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, string traceFile);
        private static Task<int> Report(CancellationToken ct, IConsole console, string traceFile)
        {
            Console.Error.WriteLine("Error: subcommand was not provided. Available subcommands:");
            Console.Error.WriteLine("    topN: Finds the top N methods on the callstack the longest.");
            return Task.FromResult(-1);
        }

        public static Command ReportCommand() =>
            new Command(
                name: "report",
                description: "Generates a report into stdout from a previously generated trace.")
                {
                    //Handler
                    HandlerDescriptor.FromDelegate((ReportDelegate)Report).GetCommandHandler(),
                    //Options
                    // FileNameArgument(),
                    TopNReportHandler.TopNCommand,
                    StatReportHandler.StatCommand
                };

        public static Argument<string> FileNameArgument() =>
            new Argument<string>("trace_filename")
            {
                Name = "tracefile",
                Description = "The file path for the trace being analyzed.",
                Arity = new ArgumentArity(1, 1)
            };
    }
}