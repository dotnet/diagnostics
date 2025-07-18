// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Internal.Common;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Stack
{
    internal static class ReportCommandHandler
    {
        /// <summary>
        /// Reports a stack trace
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="processId">The process to report the stack from.</param>
        /// <param name="name">The name of process to report the stack from.</param>
        /// <param name="duration">The duration of to trace the target for. </param>
        /// <returns></returns>
        private static async Task<int> Report(CancellationToken ct, TextWriter stdOutput, TextWriter stdError, int processId, string name, TimeSpan duration)
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
                    stdError.WriteLine("Process ID should not be negative.");
                    return -1;
                }
                else if (processId == 0)
                {
                    stdError.WriteLine("--process-id is required");
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
                EventPipeSession session = await client.StartEventPipeSessionAsync(providers, requestRundown:true, token:ct).ConfigureAwait(false);
                using (session)
                using (FileStream fs = File.OpenWrite(tempNetTraceFilename))
                {
                    Task copyTask = session.EventStream.CopyToAsync(fs, ct);
                    await Task.Delay(duration, ct).ConfigureAwait(false);
                    await session.StopAsync(ct).ConfigureAwait(false);

                    // check if rundown is taking more than 5 seconds and add comment to report
                    Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                    Task completedTask = await Task.WhenAny(copyTask, timeoutTask).ConfigureAwait(false);
                    if (completedTask == timeoutTask)
                    {
                        stdOutput.WriteLine($"# Sufficiently large applications can cause this reportCommand to take non-trivial amounts of time");
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
                        int threadId = int.Parse(threadFrame.AsSpan(template.Length, firstIndex - template.Length));

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
                        stdOutput.WriteLine($"Found {samples.Count} stacks for thread 0x{threadId:X}");
#endif
                        PrintStack(stdOutput, threadId, samples[0], stackSource);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return -1;
            }
            catch (Exception ex)
            {
                stdError.WriteLine($"[ERROR] {ex}");
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

        private static void PrintStack(TextWriter stdOutput, int threadId, StackSourceSample stackSourceSample, StackSource stackSource)
        {
            stdOutput.WriteLine($"Thread (0x{threadId:X}):");
            StackSourceCallStackIndex stackIndex = stackSourceSample.StackIndex;
            while (!stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), verboseName: false).StartsWith("Thread ("))
            {
                stdOutput.WriteLine($"  {stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), verboseName: false)}"
                    .Replace("UNMANAGED_CODE_TIME", "[Native Frames]"));
                stackIndex = stackSource.GetCallerIndex(stackIndex);
            }
            stdOutput.WriteLine();
        }

        public static Command ReportCommand()
        {
            Command reportCommand = new(
                name: "report",
                description: "reports the managed stacks from a running .NET process")
            {
                ProcessIdOption,
                NameOption,
                DurationOption
            };

            reportCommand.SetAction((parseResult, ct) => Report(ct,
                stdOutput: parseResult.Configuration.Output,
                stdError: parseResult.Configuration.Error,
                processId: parseResult.GetValue(ProcessIdOption),
                name: parseResult.GetValue(NameOption),
                duration: parseResult.GetValue(DurationOption)));

            return reportCommand;
        }

        private static readonly Option<TimeSpan> DurationOption =
            new("--duration")
            {
                Description = @"When specified, will trace for the given timespan and then automatically stop the trace. Provided in the form of dd:hh:mm:ss.",
                DefaultValueFactory = _ => TimeSpan.FromMilliseconds(10),
                Hidden = true
            };

        public static readonly Option<int> ProcessIdOption =
            new("--process-id", "-p")
            {
                Description = "The process id to report the stack."
            };

        public static readonly Option<string> NameOption =
            new("--name", "-n")
            {
                Description = "The name of the process to report the stack."
            };
    }
}
