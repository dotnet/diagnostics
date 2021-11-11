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
    internal static class ReportCommandHandler 
    {
        static List<string> unwantedMethodNames = new List<string>() { "ROOT", "Thread (" , "Process"};

        //Create an extension function to help 
        public static List<CallTreeNodeBase> ByIDSortedInclusiveMetric(this CallTree callTree) 
        {
            var ret = new List<CallTreeNodeBase>(callTree.ByID);
            ret.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));
            return ret;
        }
        delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, string traceFile);
        private static Task<int> Report(CancellationToken ct, IConsole console, string traceFile)
        {
            Console.Error.WriteLine("Error: subcommand was not provided. Available subcommands:");
            Console.Error.WriteLine("    topN: Finds the top N methods on the callstack the longest.");
            return Task.FromResult(-1);
        }

        delegate Task<int> TopNReportDelegate(CancellationToken ct, IConsole console, string traceFile, int n, bool inclusive);
        private static async Task<int> TopNReport(CancellationToken ct, IConsole console, string traceFile, int number, bool inclusive) 
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
                        ExcludeRegExs = "CPU_TIME",
                    };
                    FilterStackSource filterStack = new FilterStackSource(filterParams, stackSource, ScalingPolicyKind.ScaleToData);
                    CallTree callTree = new(ScalingPolicyKind.ScaleToData);
                    callTree.StackSource = filterStack;

                    List<CallTreeNodeBase> callTreeNodes = null;
                    //find the top n Exclusive methods
                    if(!inclusive)
                    {
                        callTreeNodes = callTree.ByIDSortedExclusiveMetric();
                    }
                    else //Find the top N Inclusive methods
                    {
                        callTreeNodes = callTree.ByIDSortedInclusiveMetric();
                    }
                    int totalElements = callTreeNodes.Count;
                        while(count < number && index < totalElements)
                        {
                            CallTreeNodeBase node = callTreeNodes[index];
                            index++;
                            if(!unwantedMethodNames.Any(node.Name.Contains))
                            {
                                nodesToReport.Add(node);
                                count++;
                            }
                        }

                    PrintReportHelper.TopNWriteToStdOut(nodesToReport, inclusive, number);
                }
                return await Task.FromResult(0);
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
            }

            return await Task.FromResult(0);
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
                        description: "Finds the top N methods on the callstack the longest.")
                        {
                            //Handler
                            HandlerDescriptor.FromDelegate((TopNReportDelegate)TopNReport).GetCommandHandler(),
                            TopNOption(),
                            InclusiveOption(),
                        }
                };
        private static Argument<string> FileNameArgument() =>
            new Argument<string>("trace_filename")
            {
                Name = "tracefile",
                Description = "The file path for the trace being analyzed.",
                Arity = new ArgumentArity(1, 1)
            };
        private static int DefaultN() => 5;
        private static Option TopNOption()
        {
            return new Option(
                aliases: new[] {"-n", "--number" },
                description: $"Gives the top N methods in the callstack.")
                {
                    Argument = new Argument<int>(name: "n", getDefaultValue: DefaultN)
                };
        }         

        private static bool DefaultIsInclusive => false;

        private static Option InclusiveOption() =>
            new Option(
                aliases: new[] { "--inclusive" },
                description: $"Output the topN methods based on inclusive time. If not specified, exclusive time is used by default")
                {
                    Argument = new Argument<bool>(name: "inclusive", getDefaultValue: () => DefaultIsInclusive)
                };
    }
}