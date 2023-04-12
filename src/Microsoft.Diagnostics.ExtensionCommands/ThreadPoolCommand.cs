// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "threadpool", Help = "Displays info about the runtime thread pool.")]
    public sealed class ThreadPoolCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [Option(Name = "-wi", Help = "Print all work items that are queued.")]
        public bool WorkItems { get; set; }

        public override void Invoke()
        {
            if (!WorkItems)
            {
                throw new ArgumentException($"-live is only applicable to -wi");
            }

            if (WorkItems)
            {
                DumpWorkItems();
            }
        }

        private void DumpWorkItems()
        {
            TableOutput output = null;

            ClrType workQueueType = Runtime.BaseClassLibrary.GetTypeByName("System.Threading.ThreadPoolWorkQueue");
            ClrType workStealingQueueType = Runtime.BaseClassLibrary.GetTypeByName("System.Threading.ThreadPoolWorkQueue+WorkStealingQueue");

            foreach (ClrObject obj in Runtime.Heap.EnumerateObjects())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (obj.Type == workQueueType)
                {
                    ClrObject workItems = obj.ReadObjectField("highPriorityWorkItems");
                    foreach (ClrObject entry in EnumerateConcurrentQueue(workItems))
                    {
                        WriteEntry(ref output, entry, isHighPri: true);
                    }

                    workItems = obj.ReadObjectField("workItems");
                    foreach (ClrObject entry in EnumerateConcurrentQueue(workItems))
                    {
                        WriteEntry(ref output, entry, isHighPri: false);
                    }

                    if (obj.Type.Fields.Any(r => r.Name == "_assignableWorkItems"))
                    {
                        workItems = obj.ReadObjectField("_assignableWorkItems");
                        foreach (ClrObject entry in EnumerateConcurrentQueue(workItems))
                        {
                            WriteEntry(ref output, entry, isHighPri: false);
                        }
                    }
                }
                else if (obj.Type == workStealingQueueType)
                {
                    ClrObject m_array = obj.ReadObjectField("m_array");
                    if (m_array.IsValid && !m_array.IsNull)
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

        private void WriteEntry(ref TableOutput output, ClrObject entry, bool isHighPri)
        {
            if (output is null)
            {
                output = new(Console, (17, ""), (16, "x12"))
                {
                    AlignLeft = true,
                };

                output.WriteRow("Queue", "");
            }

            output.WriteRow(isHighPri ? "[Global high-pri]" : "[Global]", new DmlDumpObj(entry), entry.Type?.Name);
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

        private static IEnumerable<ClrObject> EnumerateConcurrentQueue(ClrObject concurrentQueue)
        {
            if (!concurrentQueue.IsValid || concurrentQueue.IsNull)
            {
                yield break;
            }

            ClrObject curr = concurrentQueue.ReadObjectField("_head");
            while (curr.IsValid && !curr.IsNull)
            {
                ClrObject slots = curr.ReadObjectField("_slots");
                if (slots.IsValid)
                {
                    ClrArray slotsArray = slots.AsArray();
                    for (int i = 0; i < slotsArray.Length; i++)
                    {
                        ClrObject item = slotsArray.GetStructValue(i).ReadObjectField("Item");
                        if (item.IsValid && !item.IsNull)
                        {
                            yield return item;
                        }
                    }
                }

                curr = curr.ReadObjectField("_nextSegment");
            }
        }
    }
}
