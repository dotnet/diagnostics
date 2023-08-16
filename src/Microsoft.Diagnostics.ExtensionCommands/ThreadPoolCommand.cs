// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "threadpool", Help = "Displays info about the runtime thread pool.")]
    public sealed class ThreadPoolCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [Option(Name = "-ti", Help = "Print the hill climbing log.", Aliases = new string[] { "-hc" })]
        public bool PrintHillClimbingLog { get; set; }

        [Option(Name = "-wi", Help = "Print all work items that are queued.")]
        public bool PrintWorkItems { get; set; }

        public override void Invoke()
        {
            // Runtime.ThreadPool shouldn't be null unless there was a problem with the dump.
            ClrThreadPool threadPool = Runtime.ThreadPool;
            if (threadPool is null)
            {
                Console.WriteLineError("Failed to obtain ThreadPool data.");
            }
            else
            {
                Table output = new(Console, Text.WithWidth(17), Text);
                string threadpoolType = threadPool.UsingWindowsThreadPool ? "Windows" : "Portable";
                Console.WriteLine($"Using the {threadpoolType} thread pool.");

                if (threadPool.UsingWindowsThreadPool)
                {
                    output.WriteRow("Number of thread pool threads:", threadPool.ThreadCount);
                }
                else
                {
                    output.WriteRow("CPU utilization:", $"{threadPool.CpuUtilization}%");
                    output.WriteRow("Workers Total:", threadPool.ActiveWorkerThreads + threadPool.IdleWorkerThreads + threadPool.RetiredWorkerThreads);
                    output.WriteRow("Workers Running:", threadPool.ActiveWorkerThreads);
                    output.WriteRow("Workers Idle:", threadPool.IdleWorkerThreads);
                    output.WriteRow("Worker Min Limit:", threadPool.MinThreads);
                    output.WriteRow("Worker Max Limit:", threadPool.MaxThreads);
                }
                Console.WriteLine();
                ClrType threadPoolType = Runtime.BaseClassLibrary.GetTypeByName("System.Threading.ThreadPool");
                ClrStaticField usePortableIOField = threadPoolType?.GetStaticFieldByName("UsePortableThreadPoolForIO");

                // Desktop CLR work items.
                if (PrintWorkItems)
                {
                    LegacyThreadPoolWorkRequest[] requests = threadPool.EnumerateLegacyWorkRequests().ToArray();
                    if (requests.Length > 0)
                    {
                        Console.WriteLine($"Work Request in Queue: {requests.Length:n0}");
                        foreach (LegacyThreadPoolWorkRequest request in requests)
                        {
                            Console.CancellationToken.ThrowIfCancellationRequested();

                            if (request.IsAsyncTimerCallback)
                            {
                                Console.WriteLine($"    AsyncTimerCallbackCompletion TimerInfo@{request.Context:x}");
                            }
                            else
                            {
                                Console.WriteLine($"    Unknown Function: {request.Function:x}  Context: {request.Context:x}");
                            }
                        }
                    }
                }

                // We will assume that if UsePortableThreadPoolForIO field is deleted from ThreadPool then we are always
                // using C# version.
                bool usingPortableCompletionPorts = threadPool.Portable && (usePortableIOField is null || usePortableIOField.Read<bool>(usePortableIOField.Type.Module.AppDomain));
                if (!usingPortableCompletionPorts)
                {
                    output.Columns[0] = output.Columns[0].WithWidth(19);
                    output.WriteRow("Completion Total:", threadPool.TotalCompletionPorts);
                    output.WriteRow("Completion Free:", threadPool.FreeCompletionPorts);
                    output.WriteRow("Completion MaxFree:", threadPool.MaxFreeCompletionPorts);

                    output.Columns[0] = output.Columns[0].WithWidth(25);
                    output.WriteRow("Completion Current Limit:", threadPool.CompletionPortCurrentLimit);
                    output.WriteRow("Completion Min Limit:", threadPool.MinCompletionPorts);
                    output.WriteRow("Completion Max Limit:", threadPool.MaxCompletionPorts);
                    Console.WriteLine();
                }

                if (PrintHillClimbingLog)
                {
                    HillClimbingLogEntry[] hcl = threadPool.EnumerateHillClimbingLog().ToArray();
                    if (hcl.Length > 0)
                    {
                        output = new(Console, Text.WithWidth(10).WithAlignment(Align.Right), Column.ForEnum<HillClimbingTransition>(), Integer, Integer, Text.WithAlignment(Align.Right));

                        Console.WriteLine("Hill Climbing Log:");
                        output.WriteHeader("Time", "Transition", "#New Threads", "#Samples", "Throughput");

                        int end = hcl.Last().TickCount;
                        foreach (HillClimbingLogEntry entry in hcl)
                        {
                            Console.CancellationToken.ThrowIfCancellationRequested();
                            output.WriteRow($"{(entry.TickCount - end)/1000.0:0.00}", entry.StateOrTransition, entry.NewThreadCount, entry.SampleCount, $"{entry.Throughput:0.00}");
                        }

                        Console.WriteLine();
                    }
                }
            }

            // We can print managed work items even if we failed to request the ThreadPool.
            if (PrintWorkItems && (threadPool is null || threadPool.Portable))
            {
                DumpWorkItems();
            }
        }

        private void DumpWorkItems()
        {
            Table output = null;

            ClrType workQueueType = Runtime.BaseClassLibrary.GetTypeByName("System.Threading.ThreadPoolWorkQueue");
            ClrType workStealingQueueType = Runtime.BaseClassLibrary.GetTypeByName("System.Threading.ThreadPoolWorkQueue+WorkStealingQueue");

            foreach (ClrObject obj in Runtime.Heap.EnumerateObjects())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (obj.Type == workQueueType)
                {
                    if (obj.TryReadObjectField("highPriorityWorkItems", out ClrObject workItems))
                    {
                        foreach (ClrObject entry in EnumerateConcurrentQueue(workItems))
                        {
                            WriteEntry(ref output, entry, isHighPri: true);
                        }
                    }

                    if (obj.TryReadObjectField("workItems", out workItems))
                    {
                        foreach (ClrObject entry in EnumerateConcurrentQueue(workItems))
                        {
                            WriteEntry(ref output, entry, isHighPri: false);
                        }
                    }

                    if (obj.Type.Fields.Any(r => r.Name == "_assignableWorkItems"))
                    {
                        if (obj.TryReadObjectField("_assignableWorkItems", out workItems))
                        {
                            foreach (ClrObject entry in EnumerateConcurrentQueue(workItems))
                            {
                                WriteEntry(ref output, entry, isHighPri: false);
                            }
                        }
                    }
                }
                else if (obj.Type == workStealingQueueType)
                {
                    if (obj.TryReadObjectField("m_array", out ClrObject m_array) && m_array.IsValid && !m_array.IsNull)
                    {
                        ClrArray arrayView = m_array.AsArray();
                        int len = Math.Min(8192, arrayView.Length); // ensure a sensible max in case we have heap corruption

                        nuint[] buffer = arrayView.ReadValues<nuint>(0, len);
                        if (buffer != null)
                        {
                            for (int i = 0; i < len; i++)
                            {
                                if (buffer[i] != 0)
                                {
                                    ClrObject entry = Runtime.Heap.GetObject(buffer[i]);
                                    if (entry.IsValid && !entry.IsNull)
                                    {
                                        WriteEntry(ref output, entry, isHighPri: false);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void WriteEntry(ref Table output, ClrObject entry, bool isHighPri)
        {
            if (output is null)
            {
                output = new(Console, Text.WithWidth(17), DumpObj, TypeName);
                output.SetAlignment(Align.Left);
                output.WriteHeader("Queue", "Object", "Type");
            }

            output.WriteRow(isHighPri ? "[Global high-pri]" : "[Global]", entry, entry.Type);
            if (entry.IsDelegate)
            {
                ClrDelegate del = entry.AsDelegate();
                ClrDelegateTarget target = del.GetDelegateTarget();
                if (target is not null)
                {
                    Console.WriteLine($"    => {target.TargetObject.Address:x} {target.Method.Name}");
                }
            }
        }

        private IEnumerable<ClrObject> EnumerateConcurrentQueue(ClrObject concurrentQueue)
        {
            if (!concurrentQueue.IsValid || concurrentQueue.IsNull)
            {
                yield break;
            }

            if (concurrentQueue.TryReadObjectField("_head", out ClrObject curr))
            {
                while (curr.IsValid && !curr.IsNull)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    if (curr.TryReadObjectField("_slots", out ClrObject slots) && slots.IsValid && slots.IsArray)
                    {
                        ClrArray slotsArray = slots.AsArray();
                        for (int i = 0; i < slotsArray.Length; i++)
                        {
                            Console.CancellationToken.ThrowIfCancellationRequested();

                            ClrObject item = slotsArray.GetStructValue(i).ReadObjectField("Item");
                            if (item.IsValid && !item.IsNull)
                            {
                                yield return item;
                            }
                        }
                    }

                    if (!curr.TryReadObjectField("_nextSegment", out ClrObject next))
                    {
                        if (curr.Type is not null && curr.Type.GetFieldByName("_nextSegment") == null)
                        {
                            Console.WriteLineError($"Error:  Type '{curr.Type?.Name}' does not contain a '_nextSegment' field.");
                        }

                        break;
                    }

                    curr = next;
                }
            }
        }
    }
}
