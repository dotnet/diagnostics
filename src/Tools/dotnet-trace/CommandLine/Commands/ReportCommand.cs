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

namespace Microsoft.Diagnostics.Tools.Trace 
{
    internal static class ReportCommandHandler 
    {
        //Create an extension function
        public static List<CallTreeNodeBase> ByIDSortedInclusiveMetric(this CallTree callTree) 
        {
            var ret = new List<CallTreeNodeBase>(callTree.ByID);
            ret.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));
            return ret;
        }
        public static int FindStartingIndex(List<CallTreeNodeBase> ListofNodes)
        {
            int index = 0;
            int length = ListofNodes.Count;
            //TODO: What about UNMANAGED_CODE_TIME, System.Private.CoreLib!System.Threading
            List<string> UnwantedMethods = new List<string>() { "ROOT", "Thread (" };
            if(length > 0)
            {
                CallTreeNodeBase TheNode = ListofNodes[index];
                while(UnwantedMethods.Any(s => s.Equals(TheNode.Name)) && index < length) 
                {
                    index++;
                }
                return index;
            }
            else
            {
                //TODO: some kind of exception that the list has 0 or negative length
                return -1;
            }
            
        }
        delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, string traceFile, int n, bool isInclusive);
        private static async Task<int> Report(CancellationToken ct, IConsole console, string traceFile, int n, bool isInclusive) 
        {
            try 
            {
                string tempNetTraceFilename = traceFile;
                string tempEtlxFilename = TraceLog.CreateFromEventPipeDataFile(tempNetTraceFilename);
                int count = 0;
                List<CallTreeNodeBase> NodesToReport = new List<CallTreeNodeBase>();
                using (var symbolReader = new SymbolReader(System.IO.TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
                using (var eventLog = new TraceLog(tempEtlxFilename))
                {
                    var stackSource = new MutableTraceEventStackSource(eventLog)
                    {
                        OnlyManagedCodeStacks = true
                    };
                    //stackSource.DoneAddingSamples();
                    //TODO: change the computer
                    var computer = new SampleProfilerThreadTimeComputer(eventLog,symbolReader);
                    computer.GenerateThreadTimeStacks(stackSource);

                    CallTree CT = new(ScalingPolicyKind.ScaleToData);
                    CT.StackSource = stackSource;

                    List<string> UnwantedMethodNames = new List<string>() { "ROOT", "Thread (" , "Process"};
                    //find the top n Exclusive methods
                    if(!isInclusive)
                    {
                        var ExclusiveList = CT.ByIDSortedExclusiveMetric();
                        int totalElements = ExclusiveList.Count;
                        int index = FindStartingIndex(ExclusiveList);
                        while(count < n && index < totalElements) //what if n is larger than the length of the list?
                        {
                            CallTreeNodeBase node = ExclusiveList[index];
                            index++; // Name == Root or Thread? skip, ensure this is happening

                            if (node.Name == "CPU_TIME") {
                                CallerCalleeNode CallerCallee = CT.CallerCallee(node.Name);
                                IList<CallTreeNodeBase> Callers = CallerCallee.Callers;
                                //the data has already been scaled, so comparison can happen between node values
                                //What if all of the nodes' exclusive measures in Callers are greater than everything 
                                //else in the ExclusiveList?
                                    

                                //The inclusive scores of the methods that called CPU_TIME are actually their exclusive metrics
                                //because we do not care about CPU_TIME
                                float maxInclusive = 0;
                                CallTreeNodeBase maxInclusiveNode = null;
                                foreach(var methodNode in Callers) 
                                {
                                    float currentInclusiveMetric = methodNode.InclusiveMetric;
                                    if(currentInclusiveMetric > maxInclusive)
                                    {
                                        maxInclusive = currentInclusiveMetric;
                                        maxInclusiveNode = methodNode;
                                    }

                                }
                                //check to see what the Exclusive Metric is for the last node in NodesToReport
                                    //remembering that NodesToReport could be empty
                                NodesToReport.Add(maxInclusiveNode);
                                count++;

                            }
                            else if (!UnwantedMethodNames.Any(node.Name.Contains))
                            {
                                NodesToReport.Add(node);
                                count++;
                            } 
                        }
                    }
                    else //Find the top N Inclusive methods
                    {
                        var InclusiveList = CT.ByIDSortedInclusiveMetric();
                        int totalElements = InclusiveList.Count;

                        int index = FindStartingIndex(InclusiveList);
                        while(count < n && index < totalElements)
                        {
                            CallTreeNodeBase node = InclusiveList[index];
                            index++;
                            if(!UnwantedMethodNames.Any(node.Name.Contains))
                            {
                                //do we want CPU_TIME to be in the top N Inclusive? 
                                //what names do we not want to be in the top N Inclusive?
                                NodesToReport.Add(node);
                                count++;
                            }
                        }
                        Console.WriteLine("bool IsInclusive: " + isInclusive);
                    }
                    foreach(var node in NodesToReport) 
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
                description: "Generates report into stdout from a previously generated trace_file.")
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
                //aliases: new[] {"-n", "--number" },
                alias: "-n",
                description: $"Gives the top N methods in the callstack.")
                {
                    Argument = new Argument<int>(name: "n", getDefaultValue: DefaultN)
                };

        private static bool DefaultIsItInclusive => false;

        private static Option inclusiveOption() =>
            new Option(
                aliases: new[] { "-i", "--isInclusive" },
                description: $"The output with be the top n methods either inclusively or exclusively depending upon the value of {DefaultIsItInclusive}")
                {
                    Argument = new Argument<bool>(name: "inclusive", getDefaultValue: () => DefaultIsItInclusive)
                };
    }
}