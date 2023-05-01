// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.IO;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Stack
{
    internal static class ReportCommandHandler
    {
        private delegate Task<int> ReportDelegate(CancellationToken ct, IConsole console, int processId, string name, TimeSpan duration);

        /// <summary>
        /// Reports a stack trace
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to report the stack from.</param>
        /// <param name="name">The name of process to report the stack from.</param>
        /// <param name="duration">The duration of to trace the target for. </param>
        /// <returns></returns>
        private static async Task<int> Report(CancellationToken ct, IConsole console, int processId, string name, TimeSpan duration)
        {
            string tempNetTraceFilename = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".nettrace");
            string tempEtlxFilename = "";

            try
            {
                // Either processName or processId has to be specified.
                if (!string.IsNullOrEmpty(name))
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
                    console.Error.WriteLine("Process ID should not be negative.");
                    return -1;
                }
                else if (processId == 0)
                {
                    console.Error.WriteLine("--process-id is required");
                    return -1;
                }


                DiagnosticsClient client = new(processId);
                List<EventPipeProvider> providers = new()
                {
                    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational)
                };

                // collect a *short* trace with stack samples
                // the hidden '--duration' flag can increase the time of this trace in case 10ms
                // is too short in a given environment, e.g., resource constrained systems
                // N.B. - This trace INCLUDES rundown.  For sufficiently large applications, it may take non-trivial time to collect
                //        the symbol data in rundown.
                using (EventPipeSession session = client.StartEventPipeSession(providers))
                using (FileStream fs = File.OpenWrite(tempNetTraceFilename))
                {
                    Task copyTask = session.EventStream.CopyToAsync(fs);
                    await Task.Delay(duration).ConfigureAwait(false);
                    session.Stop();

                    // check if rundown is taking more than 5 seconds and add comment to report
                    Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                    Task completedTask = await Task.WhenAny(copyTask, timeoutTask).ConfigureAwait(false);
                    if (completedTask == timeoutTask)
                    {
                        console.Out.WriteLine($"# Sufficiently large applications can cause this command to take non-trivial amounts of time");
                    }
                    await copyTask.ConfigureAwait(false);
                }

                // using the generated trace file, symbolicate and compute stacks.
                tempEtlxFilename = TraceLog.CreateFromEventPipeDataFile(tempNetTraceFilename);
                using (SymbolReader symbolReader = new(TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
                using (TraceLog eventLog = new(tempEtlxFilename))
                {
                    MutableTraceEventStackSource stackSource = new(eventLog)
                    {
                        OnlyManagedCodeStacks = true
                    };

                    SampleProfilerThreadTimeComputer computer = new(eventLog, symbolReader);
                    computer.GenerateThreadTimeStacks(stackSource);

                    Dictionary<int, List<StackSourceSample>> samplesForThread = new();

                    stackSource.ForEach((sample) => {
                        StackSourceCallStackIndex stackIndex = sample.StackIndex;
                        while (!stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false).StartsWith("Thread ("))
                        {
                            stackIndex = stackSource.GetCallerIndex(stackIndex);
                        }

                        // long form for: int.Parse(threadFrame["Thread (".Length..^1)])
                        // Thread id is in the frame name as "Thread (<ID>)"
                        string template = "Thread (";
                        string threadFrame = stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false);

                        // we are looking for the first index of ) because
                        // we need to handle a thread name like: Thread (4008) (.NET IO ThreadPool Worker)
                        int firstIndex = threadFrame.IndexOf(')');
                        int threadId = int.Parse(threadFrame.Substring(template.Length, firstIndex - template.Length));

                        if (samplesForThread.TryGetValue(threadId, out List<StackSourceSample> samples))
                        {
                            samples.Add(sample);
                        }
                        else
                        {
                            samplesForThread[threadId] = new List<StackSourceSample>() { sample };
                        }
                    });

                    // For every thread recorded in our trace, print the first stack
                    foreach ((int threadId, List<StackSourceSample> samples) in samplesForThread)
                    {
#if DEBUG
                        console.Out.WriteLine($"Found {samples.Count} stacks for thread 0x{threadId:X}");
#endif
                        PrintStack(console, threadId, samples[0], stackSource);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex}");
                return -1;
            }
            finally
            {
                if (File.Exists(tempNetTraceFilename))
                {
                    File.Delete(tempNetTraceFilename);
                }

                if (File.Exists(tempEtlxFilename))
                {
                    File.Delete(tempEtlxFilename);
                }
            }

            return 0;
        }

        private static void PrintStack(IConsole console, int threadId, StackSourceSample stackSourceSample, StackSource stackSource)
        {
            console.Out.WriteLine($"Thread (0x{threadId:X}):");
            StackSourceCallStackIndex stackIndex = stackSourceSample.StackIndex;
            while (!stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), verboseName: false).StartsWith("Thread ("))
            {
                console.Out.WriteLine($"  {stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), verboseName: false)}"
                    .Replace("UNMANAGED_CODE_TIME", "[Native Frames]"));
                stackIndex = stackSource.GetCallerIndex(stackIndex);
            }
            console.Out.WriteLine();
        }

        public static Command ReportCommand() =>
            new(
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
            new(
                alias: "--duration",
                description: @"When specified, will trace for the given timespan and then automatically stop the trace. Provided in the form of dd:hh:mm:ss.")
            {
                Argument = new Argument<TimeSpan>(name: "duration-timespan", getDefaultValue: () => TimeSpan.FromMilliseconds(10)),
                IsHidden = true
            };

        public static Option ProcessIdOption() =>
            new(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id to report the stack.")
            {
                Argument = new Argument<int>(name: "pid")
            };

        public static Option NameOption() =>
            new(
                aliases: new[] { "-n", "--name" },
                description: "The name of the process to report the stack.")
            {
                Argument = new Argument<string>(name: "name")
            };
    }
}
