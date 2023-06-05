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
    [Command(Name = "gcheapstat", DefaultOptions = "GCHeapStat", Help = "Displays various GC heap stats.")]
    public class GCHeapStatCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public LiveObjectService LiveObjects { get; set; }

        [Option(Name = "-unreachable", Aliases = new string[] { "-inclUnrooted", "-iu" })]
        public bool IncludeUnreachable { get; set; }

        public override void Invoke()
        {
            HeapInfo[] heaps = Runtime.Heap.SubHeaps.Select(h => GetHeapInfo(h)).ToArray();
            bool printFrozen = heaps.Any(h => h.Frozen.Committed != 0);

            List<Column> formats = new()
            {
                Text.WithWidth(8),
                IntegerWithoutCommas,
                IntegerWithoutCommas,
                IntegerWithoutCommas,
                IntegerWithoutCommas,
                IntegerWithoutCommas,
                Text.WithWidth(8),
                Text.WithWidth(8),
                Text.WithWidth(8)
            };

            if (printFrozen)
            {
                formats.Insert(1, IntegerWithoutCommas);
            }

            Table output = new(Console, formats.ToArray());
            output.SetAlignment(Align.Left);

            // Write allocated
            WriteHeader(output, heaps, printFrozen);
            foreach (HeapInfo heapInfo in heaps)
            {
                WriteRow(output, heapInfo, (info) => info.Allocated, printFrozen);
            }

            HeapInfo total = GetTotal(heaps);
            WriteRow(output, total, (info) => info.Allocated, printFrozen);
            Console.WriteLine();

            // Write Free
            Console.WriteLine("Free space:");
            WriteHeader(output, heaps, printFrozen);
            foreach (HeapInfo heapInfo in heaps)
            {
                WriteRow(output, heapInfo, (info) => info.Free, printFrozen, printPercentage: true);
            }

            total = GetTotal(heaps);
            WriteRow(output, total, (info) => info.Free, printFrozen);
            Console.WriteLine();

            // Write unrooted
            if (IncludeUnreachable)
            {
                Console.WriteLine("Unrooted objects:");
                WriteHeader(output, heaps, printFrozen);
                foreach (HeapInfo heapInfo in heaps)
                {
                    WriteRow(output, heapInfo, (info) => info.Unrooted, printFrozen, printPercentage: true);
                }
                Console.WriteLine();

                total = GetTotal(heaps);
                WriteRow(output, total, (info) => info.Unrooted, printFrozen);
                Console.WriteLine();
            }

            // Write Committed
            Console.WriteLine("Committed space:");
            WriteHeader(output, heaps, printFrozen);
            foreach (HeapInfo heapInfo in heaps)
            {
                WriteRow(output, heapInfo, (info) => info.Committed, printFrozen);
            }

            total = GetTotal(heaps);
            WriteRow(output, total, (info) => info.Committed, printFrozen, printPercentage: false, footer: true);
            Console.WriteLine();
        }

        private static void WriteHeader(Table output, HeapInfo[] heaps, bool printFrozen)
        {
            List<string> row = new(8) { "Heap", "Gen0", "Gen1", "Gen2", "LOH", "POH" };

            if (printFrozen)
            {
                row.Add("FRZ");
            }

            bool hasEphemeral = heaps.Any(h => h.Ephemeral.Committed > 0);
            if (hasEphemeral)
            {
                row.Insert(1, "EPH");
            }

            output.WriteHeader(row.ToArray());
        }

        private static void WriteRow(Table output, HeapInfo heapInfo, Func<GenerationInfo, object> select, bool printFrozen, bool printPercentage = false, bool footer = false)
        {
            List<object> row = new(11)
            {
                heapInfo.Index == -1 ? "Total" : $"Heap{heapInfo.Index}",
                select(heapInfo.Gen0),
                select(heapInfo.Gen1),
                select(heapInfo.Gen2),
                select(heapInfo.LoH),
                select(heapInfo.PoH),
            };

            if (printFrozen)
            {
                select(heapInfo.Frozen);
            }

            bool hasEphemeral = heapInfo.Ephemeral.Committed > 0;
            if (hasEphemeral)
            {
                row.Insert(1, select(heapInfo.Ephemeral));
            }

            if (printPercentage)
            {
                ulong allocated = heapInfo.Gen0.Allocated + heapInfo.Gen1.Allocated + heapInfo.Gen2.Allocated;
                if (allocated != 0)
                {
                    ulong value = GetValue(select(heapInfo.Gen0)) + GetValue(select(heapInfo.Gen1)) + GetValue(select(heapInfo.Gen2));
                    ulong percent = value * 100 / allocated;
                    row.Add($"SOH:{percent}%");
                }
                else
                {
                    row.Add(null);
                }

                if (heapInfo.LoH.Allocated != 0)
                {
                    ulong percent = GetValue(select(heapInfo.LoH)) * 100 / heapInfo.LoH.Allocated;
                    row.Add($"LOH:{percent}%");
                }
                else
                {
                    row.Add(null);
                }

                if (heapInfo.PoH.Allocated != 0)
                {
                    ulong percent = GetValue(select(heapInfo.PoH)) * 100 / heapInfo.PoH.Allocated;
                    row.Add($"POH:{percent}%");
                }
                else
                {
                    row.Add(null);
                }
            }

            if (footer)
            {
                output.WriteFooter(row.ToArray());
            }
            else
            {
                output.WriteRow(row.ToArray());
            }
        }

        private static ulong GetValue(object value)
        {
            if (value is ulong ul)
            {
                return ul;
            }

            return 0;
        }

        private static HeapInfo GetTotal(HeapInfo[] heaps)
        {
            HeapInfo total = new();
            foreach (HeapInfo heap in heaps)
            {
                total += heap;
            }

            return total;
        }

        private HeapInfo GetHeapInfo(ClrSubHeap heap)
        {
            HeapInfo result = new()
            {
                Index = heap.Index,
            };

            foreach (ClrSegment seg in heap.Segments)
            {
                if (seg.Kind == GCSegmentKind.Ephemeral)
                {
                    result.Ephemeral.Allocated += seg.ObjectRange.Length;
                    result.Ephemeral.Committed += seg.CommittedMemory.Length;

                    foreach (ClrObject obj in seg.EnumerateObjects(carefully: true))
                    {
                        // Ignore heap corruption
                        if (!obj.IsValid)
                        {
                            continue;
                        }

                        GenerationInfo genInfo = result.GetInfoByGeneration(seg.GetGeneration(obj));
                        if (genInfo is not null)
                        {
                            if (obj.IsFree)
                            {
                                result.Ephemeral.Free += obj.Size;
                                genInfo.Free += obj.Size;
                            }
                            else
                            {
                                genInfo.Allocated += obj.Size;

                                if (IncludeUnreachable && !LiveObjects.IsLive(obj))
                                {
                                    genInfo.Unrooted += obj.Size;
                                }
                            }
                        }
                    }
                }
                else
                {
                    GenerationInfo info = seg.Kind switch
                    {
                        GCSegmentKind.Generation0 => result.Gen0,
                        GCSegmentKind.Generation1 => result.Gen1,
                        GCSegmentKind.Generation2 => result.Gen2,
                        GCSegmentKind.Large => result.LoH,
                        GCSegmentKind.Pinned => result.PoH,
                        GCSegmentKind.Frozen => result.Frozen,
                        _ => null
                    };

                    if (info is not null)
                    {
                        info.Allocated += seg.ObjectRange.Length;
                        info.Committed += seg.CommittedMemory.Length;

                        foreach (ClrObject obj in seg.EnumerateObjects(carefully: true))
                        {
                            // Ignore heap corruption
                            if (!obj.IsValid)
                            {
                                continue;
                            }

                            if (obj.IsFree)
                            {
                                info.Free += obj.Size;
                            }
                            else if (IncludeUnreachable && !LiveObjects.IsLive(obj))
                            {
                                info.Unrooted += obj.Size;
                            }
                        }
                    }
                }
            }

            return result;
        }

        private sealed class HeapInfo
        {
            public int Index;
            public GenerationInfo Ephemeral = new();
            public GenerationInfo Gen0 = new();
            public GenerationInfo Gen1 = new();
            public GenerationInfo Gen2 = new();
            public GenerationInfo LoH = new();
            public GenerationInfo PoH = new();
            public GenerationInfo Frozen = new();

            public static HeapInfo operator +(HeapInfo left, HeapInfo right)
            {
                return new()
                {
                    Index = -1,
                    Ephemeral = left.Ephemeral + right.Ephemeral,
                    Gen0 = left.Gen0 + right.Gen0,
                    Gen1 = left.Gen1 + right.Gen1,
                    Gen2 = left.Gen2 + right.Gen2,
                    LoH = left.LoH + right.LoH,
                    PoH = left.PoH + right.PoH,
                    Frozen = left.Frozen + right.Frozen,
                };
            }

            public GenerationInfo GetInfoByGeneration(Generation gen)
            {
                return gen switch
                {
                    Generation.Generation0 => Gen0,
                    Generation.Generation1 => Gen1,
                    Generation.Generation2 => Gen2,
                    Generation.Large => LoH,
                    Generation.Pinned => PoH,
                    Generation.Frozen => Frozen,
                    _ => null
                };
            }
        }

        private sealed class GenerationInfo
        {
            public ulong Allocated;
            public ulong Free;
            public ulong Unrooted;
            public ulong Committed;

            public static GenerationInfo operator +(GenerationInfo left, GenerationInfo right)
            {
                return new()
                {
                    Allocated = left.Allocated + right.Allocated,
                    Free = left.Free + right.Free,
                    Unrooted = left.Unrooted + right.Unrooted,
                    Committed = left.Committed + right.Committed
                };
            }
        }
    }
}
