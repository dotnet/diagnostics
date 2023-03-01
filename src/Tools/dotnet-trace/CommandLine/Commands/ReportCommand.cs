// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tools.Trace.CommandLine;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class ReportCommandHandler
    {
        private static List<string> unwantedMethodNames = new List<string>() { "ROOT", "Process" };

        //Create an extension function to help
        public static List<CallTreeNodeBase> ByIDSortedInclusiveMetric(this CallTree callTree)
        {
            var ret = new List<CallTreeNodeBase>(callTree.ByID);
            ret.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));
            return ret;
        }

        private delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, string traceFile);
        private static Task<int> Report(CancellationToken ct, IConsole console, string traceFile)
        {
            Console.Error.WriteLine("Error: subcommand was not provided. Available subcommands:");
            Console.Error.WriteLine("    topN: Finds the top N methods on the callstack the longest.");
            return Task.FromResult(-1);
        }

        private delegate Task<int> TopNReportDelegate(CancellationToken ct, IConsole console, string traceFile, int n, bool inclusive, bool verbose);
        private static async Task<int> TopNReport(CancellationToken ct, IConsole console, string traceFile, int number, bool inclusive, bool verbose)
        {
            try
            {
                string tempEtlxFilename = TraceLog.CreateFromEventPipeDataFile(traceFile);
                int count = 0;
                int index = 0;
                List<CallTreeNodeBase> nodesToReport = new List<CallTreeNodeBase>();
                using (var symbolReader = new SymbolReader(System.IO.TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
                using (var eventLog = new TraceLog(tempEtlxFilename))
                {
                    var stackSource = new MutableTraceEventStackSource(eventLog)
                    {
                        OnlyManagedCodeStacks = true
                    };

                    var computer = new SampleProfilerThreadTimeComputer(eventLog, symbolReader);

                    computer.GenerateThreadTimeStacks(stackSource);

                    FilterParams filterParams = new FilterParams()
                    {
                        FoldRegExs = "CPU_TIME;UNMANAGED_CODE_TIME;{Thread (}",
                    };
                    FilterStackSource filterStack = new FilterStackSource(filterParams, stackSource, ScalingPolicyKind.ScaleToData);
                    CallTree callTree = new(ScalingPolicyKind.ScaleToData);
                    callTree.StackSource = filterStack;

                    List<CallTreeNodeBase> callTreeNodes = null;

                    if (!inclusive)
                    {
                        callTreeNodes = callTree.ByIDSortedExclusiveMetric();
                    }
                    else
                    {
                        callTreeNodes = callTree.ByIDSortedInclusiveMetric();
                    }

                    int totalElements = callTreeNodes.Count;
                    while (count < number && index < totalElements)
                    {
                        CallTreeNodeBase node = callTreeNodes[index];
                        index++;
                        if (!unwantedMethodNames.Any(node.Name.Contains))
                        {
                            nodesToReport.Add(node);
                            count++;
                        }
                    }

                    PrintReportHelper.TopNWriteToStdOut(nodesToReport, inclusive, verbose);
                }
                return await Task.FromResult(0).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex}");
            }

            return await Task.FromResult(0).ConfigureAwait(false);
        }

        public static Command ReportCommand() =>
            new Command(
                name: "report",
                description: "Generates a report into stdout from a previously generated trace.")
                {
                    //Handler
                    HandlerDescriptor.FromDelegate((ReportDelegate)Report).GetCommandHandler(),
                    //Options
                    FileNameArgument(),
                    new Command(
                        name: "topN",
                        description: "Finds the top N methods that have been on the callstack the longest.")
                        {
                            //Handler
                            HandlerDescriptor.FromDelegate((TopNReportDelegate)TopNReport).GetCommandHandler(),
                            TopNOption(),
                            InclusiveOption(),
                            VerboseOption(),
                        }
                };

        private static Argument<string> FileNameArgument() =>
            new Argument<string>("trace_filename")
            {
                Name = "tracefile",
                Description = "The file path for the trace being analyzed.",
                Arity = new ArgumentArity(1, 1)
            };

        private static Option TopNOption()
        {
            return new Option(
                aliases: new[] { "-n", "--number" },
                description: $"Gives the top N methods on the callstack.")
            {
                Argument = new Argument<int>(name: "n", getDefaultValue: () => 5)
            };
        }

        private static Option InclusiveOption() =>
            new Option(
                aliases: new[] { "--inclusive" },
                description: $"Output the top N methods based on inclusive time. If not specified, exclusive time is used by default.")
            {
                Argument = new Argument<bool>(name: "inclusive", getDefaultValue: () => false)
            };

        private static Option VerboseOption() =>
            new Option(
                aliases: new[] { "-v", "--verbose" },
                description: $"Output the parameters of each method in full. If not specified, parameters will be truncated.")
            {
                Argument = new Argument<bool>(name: "verbose", getDefaultValue: () => false)
            };
    }
}
