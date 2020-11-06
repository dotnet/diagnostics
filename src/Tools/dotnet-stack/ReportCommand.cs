// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Rendering;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace Microsoft.Diagnostics.Tools.Stack
{
    internal static class ReportCommandHandler
    {
        delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, int processId, string name, TimeSpan duration);

        /// <summary>
        /// Reports a stack trace
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to report the stack from.</param>
        /// <param name="name">The name of process to report the stack from.</param>
        /// <param name="output">The output path for the collected trace data.</param>
        /// <param name="duration">The duration of to trace the target for. </param>
        /// <returns></returns>
        private static async Task<int> Report(CancellationToken ct, IConsole console, int processId, string name, TimeSpan duration)
        {
            string tempNetTraceFilename = Path.GetRandomFileName() + ".nettrace";
            string tempEtlxFilename = "";

            try
            {
                // Either processName or processId has to be specified.
                if (name != null)
                {
                    if (processId != 0)
                    {
                        Console.WriteLine("Can only specify either --name or --process-id option.");
                        return -1;
                    }
                    processId = CommandUtils.FindProcessIdWithName(name);
                    if (processId < 0)
                    {
                        return -1;
                    }
                }
                if (processId < 0)
                {
                    Console.Error.WriteLine("Process ID should not be negative.");
                    return -1;
                }
                else if (processId == 0)
                {
                    Console.Error.WriteLine("--process-id is required");
                    return -1;
                }


                var client = new DiagnosticsClient(processId);
                var providers = new List<EventPipeProvider>()
                {
                    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational)
                };

                // collect a *short* trace with stack samples
                // the hidden duration flag can increase the time of this trace in case 10ms
                // is too short in a given environment
                // N.B. - This trace INCLUDES rundown.  For sufficiently large applications, it may take non-trivial time to collect
                //        the symbol data in rundown.
                using (EventPipeSession session = client.StartEventPipeSession(providers))
                using (FileStream fs = File.OpenWrite(tempNetTraceFilename))
                {
                    Task copyTask = session.EventStream.CopyToAsync(fs);
                    await Task.Delay(duration);
                    session.Stop();
                    await copyTask;
                }

                // using the generated trace file, symbolocate and compute stacks.
                tempEtlxFilename = TraceLog.CreateFromEventPipeDataFile(tempNetTraceFilename);
                using (var symbolReader = new SymbolReader(System.IO.TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
                using (var eventLog = new TraceLog(tempEtlxFilename))
                {
                    var stackSource = new MutableTraceEventStackSource(eventLog)
                    {
                        OnlyManagedCodeStacks = true
                    };

                    var computer = new SampleProfilerThreadTimeComputer(eventLog, symbolReader);
                    computer.GenerateThreadTimeStacks(stackSource);

                    var samplesForThread = new Dictionary<int, List<StackSourceSample>>();

                    stackSource.ForEach((sample) =>
                    {
                        var stackIndex = sample.StackIndex;
                        while (!stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false).StartsWith("Thread ("))
                            stackIndex = stackSource.GetCallerIndex(stackIndex);

                        // long form for: int.Parse(threadFrame["Thread (".Length..^1)])
                        // Thread id is in the frame name as "Thread (<ID>)"
                        string template = "Thread (";
                        string threadFrame = stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false);
                        int threadId = int.Parse(threadFrame.Substring(template.Length, threadFrame.Length - (template.Length + 1)));
                        if (samplesForThread.TryGetValue(threadId, out var samples))
                        {
                            samples.Add(sample);
                        }
                        else
                        {
                            samplesForThread[threadId] = new List<StackSourceSample>() { sample };
                        }
                    });

                    foreach (var (threadId, samples) in samplesForThread)
                    {
#if DEBUG
                        Console.WriteLine($"Found {samples.Count} stacks for thread 0x{threadId:X}");
#endif
                        PrintStack(threadId, samples[0], stackSource);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return -1;
            }
            finally
            {
                if (File.Exists(tempNetTraceFilename))
                    File.Delete(tempNetTraceFilename);
                if (File.Exists(tempEtlxFilename))
                    File.Delete(tempEtlxFilename);

                if (console.GetTerminal() != null)
                    Console.CursorVisible = true;
            }

            return 0;
        }

        private static void PrintStack(int threadId, StackSourceSample stackSourceSample, StackSource stackSource)
        {
            Console.WriteLine($"Thread (0x{threadId:X}):");
            var stackIndex = stackSourceSample.StackIndex;
            while (!stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), verboseName: false).StartsWith("Thread ("))
            {
                Console.WriteLine($"  {stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), verboseName: false)}"
                    .Replace("UNMANAGED_CODE_TIME", "[Native Frames]")
                    .Replace("int32", "int")
                    .Replace("unsigned int32", "uint")
                    .Replace("int64", "long")
                    .Replace("unsigned int64", "ulong"));
                stackIndex = stackSource.GetCallerIndex(stackIndex);
            }
            Console.WriteLine();
        }

        public static Command ReportCommand() =>
            new Command(
                name: "report",
                description: "reports the managed stacks from a running .NET process") 
            {
                // Handler
                HandlerDescriptor.FromDelegate((ReportDelegate)Report).GetCommandHandler(),
                // Options
                ProcessIdOption(),
                NameOption(),
                DurationOption()
            };

        private static Option DurationOption() =>
            new Option(
                alias: "--duration",
                description: @"When specified, will trace for the given timespan and then automatically stop the trace. Provided in the form of dd:hh:mm:ss.")
            {
                Argument = new Argument<TimeSpan>(name: "duration-timespan", getDefaultValue: () => TimeSpan.FromMilliseconds(10)),
                IsHidden = true
            };

        public static Option ProcessIdOption() =>
            new Option(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to collect the trace.")
            {
                Argument = new Argument<int>(name: "pid")
            };

        public static Option NameOption() =>
            new Option(
                aliases: new[] { "-n", "--name" },
                description: "The name of the process to collect the trace.")
            {
                Argument = new Argument<string>(name: "name")
            };
    }
}
