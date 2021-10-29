using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Rendering;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.StackSources;
using Diagnostics.Tracing.StackSources;

namespace Microsoft.Diagnostics.Tools.Trace 
{
    internal static class ReportCommandHandler 
    {
        static List<string> unwantedMethodNames = new List<string>() { "ROOT", "Thread (" , "Process"};
        //Create an extension function
        public static List<CallTreeNodeBase> ByIDSortedInclusiveMetric(this CallTree callTree) 
        {
            var ret = new List<CallTreeNodeBase>(callTree.ByID);
            ret.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));
            return ret;
        }
        delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, string traceFile, int number, bool inclusive);
        private static async Task<int> Report(CancellationToken ct, IConsole console, string traceFile, int number, bool inclusive) 
        {
            if (traceFile == null)
            {
                Console.Error.WriteLine("<traceFile> is required");
                return await Task.FromResult(-1);
            }
            
            try 
            {
                string tempNetTraceFilename = traceFile;
                string tempEtlxFilename = TraceLog.CreateFromEventPipeDataFile(tempNetTraceFilename);
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
                    //stackSource.DoneAddingSamples();
                    //TODO: change the computer
                    //WE NEED TO FILTER OUT CPU_TIME STACKS
                    var computer = new SampleProfilerThreadTimeComputer(eventLog,symbolReader);//change this to not insert the CPU timeframe
                    //do it on a boolean flag generate stacks without inserting CPU time frame
                    computer.GenerateThreadTimeStacks(stackSource);//modify GenerateThreadTimeStacks to do something
                    //the reads the event log and knows how to extract the stacks from it
                    //inside AddCPU function pass in something that prevents adding the CPU frame
                    //filter out the CPU frame? nope because of GetSumByID
                    FilterParams filterParams = new FilterParams();
                    filterParams.ExcludeRegExs = "CPU_TIME";
                    FilterStackSource filterStack = new FilterStackSource(filterParams, stackSource, ScalingPolicyKind.ScaleToData);
                    CallTree callTree = new(ScalingPolicyKind.ScaleToData);
                    callTree.StackSource = filterStack;

                    
                    //find the top n Exclusive methods
                    if(!inclusive)
                    {
                        var exclusiveList = callTree.ByIDSortedExclusiveMetric();
                        int totalElements = exclusiveList.Count;
                        while(count < number && index < totalElements) //what if n is larger than the length of the list?
                        {
                            CallTreeNodeBase node = exclusiveList[index];
                            index++; // Name == Root or Thread? skip, ensure this is happening

                            // if (node.Name == "CPU_TIME") {
                            //     Console.WriteLine("CPU_TIME it is");
                            //     CallerCalleeNode CallerCallee = CT.CallerCallee(node.Name);
                            //     IList<CallTreeNodeBase> Callers = CallerCallee.Callers;
                            //     //the data has already been scaled, so comparison can happen between node values
                            //     //What if all of the nodes' exclusive measures in Callers are greater than everything 
                            //     //else in the exclusiveList?
                                    

                            //     //The inclusive scores of the methods that called CPU_TIME are actually their exclusive metrics
                            //     //because we do not care about CPU_TIME
                            //     float maxInclusive = 0;
                            //     CallTreeNodeBase maxInclusiveNode = null;
                            //     foreach(var methodNode in Callers) 
                            //     {
                            //         //methodNode.GetSamples??
                            //         float currentInclusiveMetric = methodNode.InclusiveMetric;
                            //         if(currentInclusiveMetric > maxInclusive)
                            //         {
                            //             maxInclusive = currentInclusiveMetric;
                            //             maxInclusiveNode = methodNode;
                            //         }

                            //     }
                            //     //check to see what the Exclusive Metric is for the last node in NodesToReport
                            //         //remembering that NodesToReport could be empty
                            //     nodesToReport.Add(maxInclusiveNode);
                            //     count++;

                            // }
                            if (!unwantedMethodNames.Any(node.Name.StartsWith))
                            {
                                nodesToReport.Add(node);
                                count++;
                            } 
                        }
                    }
                    else //Find the top N Inclusive methods
                    {
                        var InclusiveList = callTree.ByIDSortedInclusiveMetric();
                        int totalElements = InclusiveList.Count;
                        while(count < number && index < totalElements)
                        {
                            CallTreeNodeBase node = InclusiveList[index];
                            index++;
                            if(!unwantedMethodNames.Any(node.Name.Contains))
                            {
                                //do we want CPU_TIME to be in the top N Inclusive? 
                                //what names do we not want to be in the top N Inclusive?
                                nodesToReport.Add(node);
                                count++;
                            }
                        }
                        Console.WriteLine("bool IsInclusive: " + inclusive);
                    }
                    foreach(var node in nodesToReport) 
                    {
                        Console.WriteLine(node.ToString());
                    }
                }
                return await Task.FromResult(0);
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
            }
            // finally 
            // {
            //     
            // }
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
                    topNOption(),
                    inclusiveOption()
                };
        private static Argument<string> FileNameArgument() =>
            new Argument<string>("trace_filename")
            {
                Name = "tracefile",
                Description = "The file to read trace from to create report.",
                Arity = new ArgumentArity(0, 1)
            };
        private static int DefaultN() => 5;
        private static Option topNOption() =>
            new Option(
                aliases: new[] {"-n", "--number" },
                //alias: "-n",
                description: $"Gives the top N methods in the callstack.")
                {
                    Argument = new Argument<int>(name: "n", getDefaultValue: DefaultN)
                };

        private static bool DefaultIsInclusive => false;

        private static Option inclusiveOption() =>
            new Option(
                aliases: new[] { "--inclusive" },
                description: $"Output the topN methods based on inclusive time. If not specified, exclusive time is used by default")
                {
                    Argument = new Argument<bool>(name: "inclusive", getDefaultValue: () => DefaultIsInclusive)
                };
    }
}