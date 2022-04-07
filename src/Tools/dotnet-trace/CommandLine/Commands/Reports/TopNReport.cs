// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tools.Trace.CommandLine;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class TopNReportHandler
    {
        private static readonly HashSet<string> UnwantedMethodNames = new() { "ROOT", "Process"};

        //Create an extension function to help 
        private static List<CallTreeNodeBase> ByIDSortedInclusiveMetric(this CallTree callTree) 
        {
            var ret = new List<CallTreeNodeBase>(callTree.ByID);
            ret.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));
            return ret;
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

                    var computer = new SampleProfilerThreadTimeComputer(eventLog,symbolReader);

                    computer.GenerateThreadTimeStacks(stackSource);

                    FilterParams filterParams = new FilterParams()
                    {
                        FoldRegExs = "CPU_TIME;UNMANAGED_CODE_TIME;{Thread (}",
                    };
                    FilterStackSource filterStack = new FilterStackSource(filterParams, stackSource, ScalingPolicyKind.ScaleToData);
                    CallTree callTree = new(ScalingPolicyKind.ScaleToData);
                    callTree.StackSource = filterStack;

                    List<CallTreeNodeBase> callTreeNodes = null;

                    if(!inclusive)
                    {
                        callTreeNodes = callTree.ByIDSortedExclusiveMetric();
                    }
                    else
                    {
                        callTreeNodes = callTree.ByIDSortedInclusiveMetric();
                    }

                    int totalElements = callTreeNodes.Count;
                        while(count < number && index < totalElements)
                        {
                            CallTreeNodeBase node = callTreeNodes[index];
                            index++;
                            if(!UnwantedMethodNames.Contains(node.Name))
                            {
                                nodesToReport.Add(node);
                                count++;
                            }
                        }

                    PrintReportHelper.TopNWriteToStdOut(nodesToReport, inclusive, verbose);
                }
                return await Task.FromResult(0);
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex}");
            }

            return await Task.FromResult(0);
        }

        private static Option TopNOption() =>
            new Option(
                aliases: new[] {"-n", "--number" },
                description: $"Gives the top N methods on the callstack.")
                {
                    Argument = new Argument<int>(name: "n", getDefaultValue: () => 5)
                };

        private static Option InclusiveOption() =>
            new Option(
                aliases: new[] { "--inclusive" },
                description: $"Output the top N methods based on inclusive time. If not specified, exclusive time is used by default.")
                {
                    Argument = new Argument<bool>(name: "inclusive", getDefaultValue: () => false)
                };

        public static Option VerboseOption() =>
            new Option(
                aliases: new[] {"-v", "--verbose"},
                description: $"Output the parameters of each method in full. If not specified, parameters will be truncated.")
                {
                    Argument = new Argument<bool>(name: "verbose", getDefaultValue: () => false)
                };

        public static Command TopNCommand =>
            new Command(
                name: "topN",
                description: "Finds the top N methods that have been on the callstack the longest.")
                {
                    //Handler
                    HandlerDescriptor.FromDelegate((TopNReportDelegate)TopNReport).GetCommandHandler(),
                    TopNOption(),
                    InclusiveOption(),
                    VerboseOption(),
                    ReportCommandHandler.FileNameArgument()
                };
    }
}