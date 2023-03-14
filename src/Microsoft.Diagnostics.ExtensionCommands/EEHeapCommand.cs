// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = CommandName, Help = "Displays information about native memory that CLR has allocated.")]
    public class EEHeapCommand : CommandBase
    {
        private const string CommandName = "eeheap";

        private HeapWithFilters HeapWithFilters { get; set; }

        // Don't use the word "Total" if we have filtered out entries
        private string TotalString => HeapWithFilters.HasFilters ? "Partial" : "Total";

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [Option(Name = "-gc", Help = "Only display the GC.")]
        public bool ShowGC { get; set; }

        [Option(Name = "-loader", Help = "Only display the Loader.")]
        public bool ShowLoader { get; set; }

        [Option(Name = "-heap")]
        public int GCHeap { get; set; } = -1;

        [Option(Name = "-segment")]
        public string Segment { get; set; }

        [Argument(Help = "Optional memory ranges in the form of: [Start [End]]")]
        public string[] MemoryRange { get; set; }

        public override void Invoke()
        {
            HeapWithFilters = new(Runtime.Heap)
            {
                // The user may want to filter loader regions by address
                ThrowIfNoMatchingGCRegions = false
            };

            if (GCHeap >= 0)
            {
                HeapWithFilters.GCHeap = GCHeap;
            }

            if (!string.IsNullOrWhiteSpace(Segment))
            {
                HeapWithFilters.FilterBySegmentHex(Segment);
            }

            if (MemoryRange is not null)
            {
                HeapWithFilters.FilterByStringMemoryRange(MemoryRange, CommandName);
            }

            PrintOneRuntime(Runtime);
        }

        private ulong PrintOneRuntime(ClrRuntime clrRuntime)
        {
            StringBuilder stringBuilder = null;
            TableOutput output = new(Console, (21, "x12"), (0, "x12"))
            {
                AlignLeft = true
            };

            HashSet<ulong> seen = new();

            ulong totalSize = 0;

            if (ShowLoader || !ShowGC)
            {
                totalSize += PrintAppDomains(output, clrRuntime, seen);
                totalSize += PrintCodeHeaps(output, clrRuntime);
                totalSize += PrintModuleThunkTable(output, ref stringBuilder, clrRuntime);
                totalSize += PrintModuleLoaderAllocators(output, ref stringBuilder, clrRuntime, seen);
            }

            if (ShowGC || !ShowLoader)
            {
                totalSize += PrintGCHeap(clrRuntime);
            }

            // Only print the total bytes if we walked everything.
            if (!ShowGC && !ShowLoader)
            {
                WriteLine();
                WriteLine($"{TotalString} bytes consumed by CLR: {FormatMemorySize(totalSize, "0")}");
                WriteLine();
            }

            return totalSize;
        }

        private ulong PrintAppDomains(TableOutput output, ClrRuntime clrRuntime, HashSet<ulong> loaderAllocatorsSeen)
        {
            Console.WriteLine("Loader Heap:");
            WriteDivider();

            ulong totalBytes = 0;

            totalBytes += PrintAppDomain(output, clrRuntime.SystemDomain, "System Domain:", loaderAllocatorsSeen);
            totalBytes += PrintAppDomain(output, clrRuntime.SharedDomain, "Shared Domain:", loaderAllocatorsSeen);

            for (int i = 0; i < clrRuntime.AppDomains.Length; i++)
            {
                ClrAppDomain appDomain = clrRuntime.AppDomains[i];
                totalBytes += PrintAppDomain(output, appDomain, $"Domain {i + 1}:", loaderAllocatorsSeen);
            }

            return totalBytes;
        }

        private ulong PrintAppDomain(TableOutput output, ClrAppDomain appDomain, string name, HashSet<ulong> loaderAllocatorsSeen)
        {
            if (appDomain is null)
            {
                return 0;
            }

            output.WriteRow(name, appDomain.Address);

            // Starting on .Net 8 and beyond, we now have the LoaderAllocator for each domain.  If we've previously
            // seen this LoaderAllocator, we won't print it again.  We also need to keep track of all LoaderAllocators
            // we've seen so that we know when a ClrModule has a new/unique one. This does change the output of
            // !eeheap, as we will no longer print duplicate heaps for new AppDomains.

            if (appDomain.LoaderAllocator != 0)
            {
                output.WriteRow("LoaderAllocator:", appDomain.LoaderAllocator);

                loaderAllocatorsSeen ??= new();
                if (!loaderAllocatorsSeen.Add(appDomain.LoaderAllocator))
                {
                    return 0;
                }
            }

            IOrderedEnumerable<IGrouping<NativeHeapKind, ClrNativeHeapInfo>> filteredHeapsByKind = from heap in appDomain.EnumerateLoaderAllocatorHeaps()
                                                                                                   where IsIncludedInFilter(heap)
                                                                                                   where loaderAllocatorsSeen.Add(heap.Address)
                                                                                                   group heap by heap.Kind into g
                                                                                                   orderby GetSortOrder(g.Key)
                                                                                                   select g;

            return PrintAppDomainHeapsByKind(output, filteredHeapsByKind);
        }

        private static int GetSortOrder(NativeHeapKind key)
        {
            // Order heaps in a similar order to the old !eeheap
            return key switch
            {
                NativeHeapKind.LowFrequencyHeap => 0,
                NativeHeapKind.HighFrequencyHeap => 1,
                NativeHeapKind.StubHeap => 2,
                NativeHeapKind.ExecutableHeap => 3,
                NativeHeapKind.FixupPrecodeHeap => 4,
                NativeHeapKind.NewStubPrecodeHeap => 5,

                NativeHeapKind.IndirectionCellHeap => 6,
                NativeHeapKind.LookupHeap => 7,
                NativeHeapKind.ResolveHeap => 8,
                NativeHeapKind.DispatchHeap => 9,
                NativeHeapKind.CacheEntryHeap => 10,
                NativeHeapKind.VtableHeap => 11,

                _ => 100 + (int)key
            };
        }

        private ulong PrintAppDomainHeapsByKind(TableOutput output, IOrderedEnumerable<IGrouping<NativeHeapKind, ClrNativeHeapInfo>> filteredHeapsByKind)
        {
            // Just build and print the table.
            ulong totalSize = 0;
            ulong totalWasted = 0;
            StringBuilder text = new(512);

            foreach (IGrouping<NativeHeapKind, ClrNativeHeapInfo> item in filteredHeapsByKind)
            {
                text.Clear();
                NativeHeapKind kind = item.Key;
                ulong heapSize = 0;
                ulong heapWasted = 0;

                foreach (ClrNativeHeapInfo heap in item)
                {
                    if (text.Length > 0)
                    {
                        text.Append(' ');
                    }

                    (ulong size, ulong wasted) = CalculateSizeAndWasted(text, heap);

                    heapSize += size;
                    heapWasted += wasted;
                }

                text.Append(' ');
                WriteSizeAndWasted(text, heapSize, heapWasted);
                text.Append('.');

                output.WriteRow($"{kind}:", text);

                totalSize += heapSize;
                totalWasted += heapWasted;
            }

            text.Clear();

            if (totalSize > 0)
            {
                WriteSizeAndWasted(text, totalSize, totalWasted);
                text.Append('.');
                output.WriteRow($"{TotalString} size:", text);
            }
            else
            {
                Console.WriteLine("No unique loader heaps found.");
            }

            WriteDivider();
            return totalSize;
        }

        private ulong PrintCodeHeaps(TableOutput output, ClrRuntime clrRuntime)
        {
            ulong totalSize = 0;
            StringBuilder text = new(512);
            foreach (ClrJitManager jitManager in clrRuntime.EnumerateJitManagers())
            {
                output.WriteRow("JIT Manager:", jitManager.Address);

                IEnumerable<ClrNativeHeapInfo> heaps = jitManager.EnumerateNativeHeaps().Where(IsIncludedInFilter).OrderBy(r => r.Kind).ThenBy(r => r.Address);

                ulong jitMgrSize = 0, jitMgrWasted = 0;
                foreach (ClrNativeHeapInfo heap in heaps)
                {
                    text.Clear();

                    (ulong actualSize, ulong wasted) = CalculateSizeAndWasted(text, heap);
                    jitMgrSize += actualSize;
                    jitMgrWasted += wasted;

                    text.Append(' ');
                    WriteSizeAndWasted(text, actualSize, wasted);
                    text.Append('.');

                    output.WriteRow($"{heap.Kind}:", text);
                }

                text.Clear();
                WriteSizeAndWasted(text, jitMgrSize, jitMgrWasted);
                text.Append('.');

                output.WriteRow($"{TotalString} size:", text);
                WriteDivider();

                totalSize += jitMgrSize;
            }

            return totalSize;
        }

        private bool IsIncludedInFilter(ClrNativeHeapInfo info)
        {
            // ClrNativeHeapInfo is only filtered by memory range (not heap or segment).
            if (HeapWithFilters.MemoryRange is not MemoryRange filterRange)
            {
                // no filter, so include everything
                return true;
            }

            if (filterRange.Contains(info.Address))
            {
                return true;
            }

            if (info.Size is ulong size && size > 0)
            {
                // Check for the last valid address in the range
                return filterRange.Contains(info.Address + size - 1);
            }

            return false;
        }

        private (ulong Size, ulong Wasted) CalculateSizeAndWasted(StringBuilder sb, ClrNativeHeapInfo heap)
        {
            sb.Append(heap.Address.ToString("x12"));

            if (heap.Size is ulong size)
            {
                sb.Append('(');
                sb.Append(size.ToString("x"));
                sb.Append(':');
                ulong actualSize = GetActualSize(heap.Address, size);
                sb.Append(actualSize.ToString("x"));
                sb.Append(')');

                ulong wasted = 0;
                if (actualSize < size && !heap.IsCurrentBlock)
                {
                    wasted = size - actualSize;
                }

                return (actualSize, wasted);
            }

            return (0, 0);
        }

        private ulong PrintModuleThunkTable(TableOutput output, ref StringBuilder text, ClrRuntime clrRuntime)
        {
            IEnumerable<ClrModule> modulesWithThunks = clrRuntime.EnumerateModules().Where(r => r.ThunkHeap != 0);
            if (!modulesWithThunks.Any())
            {
                return 0;
            }

            WriteDivider();
            WriteLine("Module Thunk heaps:");

            return PrintModules(output, ref text, modulesWithThunks);
        }

        private ulong PrintModuleLoaderAllocators(TableOutput output, ref StringBuilder text, ClrRuntime clrRuntime, HashSet<ulong> loaderAllocatorsSeen)
        {
            // On .Net Core, modules share their LoaderAllocator with their AppDomain (and AppDomain shares theirs
            // with SystemDomain).  Only collectable assemblies have unique loader allocators, and that's what we
            // are essentially enumerating here.
            IEnumerable<ClrModule> collectable = from module in clrRuntime.EnumerateModules()
                                                 where module.LoaderAllocator != 0
                                                 where loaderAllocatorsSeen is null || loaderAllocatorsSeen.Contains(module.LoaderAllocator)
                                                 select module;

            if (!collectable.Any())
            {
                return 0;
            }

            WriteDivider();
            WriteLine("Module LoaderAllocators:");

            return PrintModules(output, ref text, collectable);
        }

        private ulong PrintModules(TableOutput output, ref StringBuilder text, IEnumerable<ClrModule> modules)
        {
            text ??= new(128);
            ulong totalSize = 0, totalWasted = 0;
            foreach (ClrModule module in modules)
            {
                ulong moduleSize = 0, moduleWasted = 0;

                text.Clear();
                foreach (ClrNativeHeapInfo info in module.EnumerateThunkHeap().Where(IsIncludedInFilter))
                {
                    if (text.Length > 0)
                    {
                        text.Append(' ');
                    }

                    (ulong actualSize, ulong wasted) = CalculateSizeAndWasted(text, info);

                    moduleSize += actualSize;
                    moduleWasted += wasted;

                }

                text.Append(' ');
                WriteSizeAndWasted(text, moduleSize, moduleWasted);
                text.Append('.');

                totalSize += moduleSize;
                totalWasted += moduleWasted;
            }

            text.Clear();
            WriteSizeAndWasted(text, totalSize, totalWasted);
            output.WriteRow($"{TotalString} size:", text);

            return totalSize;
        }

        private static void WriteSizeAndWasted(StringBuilder sb, ulong heapSize, ulong heapWasted)
        {
            sb.Append("Size: ");
            sb.Append(FormatMemorySize(heapSize));
            sb.Append(" bytes total");

            if (heapWasted > 0)
            {
                sb.Append(", ");
                sb.Append(FormatMemorySize(heapWasted));
                sb.Append(" bytes wasted");
            }
        }

        private ulong GetActualSize(ulong address, ulong size)
        {
            const uint PageSize = 0x1000;
            if (size > 0)
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent((int)PageSize);

                ulong end = address + size;
                ulong actualSize = 0;

                while (address < end && MemoryService.ReadMemory(address, buffer, buffer.Length, out _))
                {
                    actualSize += PageSize;
                    address += PageSize;
                }

                ArrayPool<byte>.Shared.Return(buffer);
                return actualSize;
            }

            return 0;
        }

        private ulong PrintGCHeap(ClrRuntime clrRuntime)
        {
            Console.WriteLine();
            ClrHeap heap = clrRuntime.Heap;

            int pointerWidth = 16;
            string pointerToStringFormat = "x16";
            (int pointerWidth, string pointerToStringFormat) pointerFormat = (pointerWidth, pointerToStringFormat);

            int sizeWidth = Math.Max(15, heap.Segments.Max(seg => FormatMemorySize(seg.CommittedMemory.Length).Length));
            (int sizeWidth, string) sizeFormat = (sizeWidth, "");

            TableOutput gcOutput = new(Console, pointerFormat, pointerFormat, pointerFormat, pointerFormat, sizeFormat, sizeFormat);

            WriteDivider('=');
            Console.WriteLine($"Number of GC Heaps: {heap.SubHeaps.Length}");
            WriteDivider();

            foreach (ClrSubHeap gc_heap in HeapWithFilters.EnumerateFilteredSubHeaps())
            {
                if (heap.IsServer)
                {
                    Console.Write("Heap ");
                    Console.WriteDmlExec(gc_heap.Index.ToString(), $"!dumpheap -heap {gc_heap.Index}");
                    Console.WriteLine(" ({gc_heap.Address:x16})");
                }

                if (!gc_heap.HasRegions)
                {
                    for (int i = 0; i <= 2 && i < gc_heap.GenerationTable.Length; i++)
                    {
                        ClrSegment seg = heap.GetSegmentByAddress(gc_heap.GenerationTable[i].AllocationStart);
                        MemoryRange range = default;
                        if (seg is not null)
                        {
                            range = i switch
                            {
                                0 => seg.Generation0,
                                1 => seg.Generation1,
                                2 => seg.Generation2,
                                _ => default
                            };
                        }

                        if (range.Length > 0)
                        {
                            Console.Write($"generation {i} starts at ");
                            Console.WriteDmlExec(gc_heap.GenerationTable[i].AllocationStart.ToString("x"), $"!dumpheap {range.Start:x} {range.End:x}");
                            Console.WriteLine();
                        }
                        else
                        {
                            Console.WriteLine($"generation {i} starts at {gc_heap.GenerationTable[i].AllocationStart:x}");
                        }
                    }

                    Console.Write("ephemeral segment allocation context: ");
                    if (gc_heap.AllocationContext.Length > 0)
                    {
                        Console.WriteLine($"(0x{gc_heap.AllocationContext.Start:x}, 0x{gc_heap.AllocationContext.End})");
                    }
                    else
                    {
                        Console.WriteLine("none");
                    }
                }

                // Print gen 0-2
                Console.WriteLine("Small object heap");
                WriteSegmentHeader(gcOutput);

                bool[] needToPrintGen = new bool[] { gc_heap.HasRegions, gc_heap.HasRegions, gc_heap.HasRegions };
                IEnumerable<ClrSegment> ephemeralSegments = HeapWithFilters.EnumerateFilteredSegments(gc_heap).Where(seg => seg.Kind == GCSegmentKind.Ephemeral || (seg.Kind >= GCSegmentKind.Generation0 && seg.Kind <= GCSegmentKind.Generation2));
                IEnumerable<ClrSegment> segments = ephemeralSegments.OrderBy(seg => seg.Kind).ThenBy(seg => seg.Start);
                foreach (ClrSegment segment in segments)
                {
                    int genIndex = segment.Kind - GCSegmentKind.Generation0;
                    if (genIndex >= 0 && genIndex < needToPrintGen.Length && needToPrintGen[genIndex])
                    {
                        Console.WriteLine($"generation {genIndex}:");
                        needToPrintGen[genIndex] = false;
                    }

                    WriteSegment(gcOutput, segment);
                }

                // print frozen object heap
                segments = HeapWithFilters.EnumerateFilteredSegments(gc_heap).Where(seg => seg.Kind == GCSegmentKind.Frozen).OrderBy(seg => seg.Start);
                if (segments.Any())
                {
                    Console.WriteLine("Frozen object heap");
                    WriteSegmentHeader(gcOutput);

                    foreach (ClrSegment segment in segments)
                    {
                        WriteSegment(gcOutput, segment);
                    }
                }

                // print large object heap
                if (gc_heap.HasRegions || gc_heap.GenerationTable.Length <= 3)
                {
                    Console.WriteLine("Large object heap");
                }
                else
                {
                    Console.WriteLine($"Large object heap starts at {gc_heap.GenerationTable[3].AllocationStart:x}");
                }

                segments = HeapWithFilters.EnumerateFilteredSegments(gc_heap).Where(seg => seg.Kind == GCSegmentKind.Large).OrderBy(seg => seg.Start);
                WriteSegmentHeader(gcOutput);

                foreach (ClrSegment segment in segments)
                {
                    WriteSegment(gcOutput, segment);
                }

                // print pinned object heap
                segments = HeapWithFilters.EnumerateFilteredSegments(gc_heap).Where(seg => seg.Kind == GCSegmentKind.Pinned).OrderBy(seg => seg.Start);
                if (segments.Any())
                {
                    if (gc_heap.HasRegions || gc_heap.GenerationTable.Length <= 3)
                    {
                        Console.WriteLine("Pinned object heap");
                    }
                    else
                    {
                        Console.WriteLine($"Pinned object heap starts at {gc_heap.GenerationTable[4].AllocationStart:x}");
                    }

                    WriteSegmentHeader(gcOutput);

                    foreach (ClrSegment segment in segments)
                    {
                        WriteSegment(gcOutput, segment);
                    }
                }

                if (HeapWithFilters.HasFilters)
                {
                    Console.WriteLine($"{TotalString} Allocated Size:              Size: {FormatMemorySize((ulong)HeapWithFilters.EnumerateFilteredSegments(gc_heap).Sum(r => (long)r.ObjectRange.Length))} bytes.");
                    Console.WriteLine($"{TotalString} Committed Size:              Size: {FormatMemorySize((ulong)HeapWithFilters.EnumerateFilteredSegments(gc_heap).Sum(r => (long)r.CommittedMemory.Length))} bytes.");
                }

                Console.WriteLine("------------------------------");
            }

            string prefix = "";
            if (HeapWithFilters.HasFilters)
            {
                prefix = "Partial ";
            }

            ulong totalAllocated = (ulong)HeapWithFilters.EnumerateFilteredSegments().Sum(r => (long)r.ObjectRange.Length);
            ulong totalCommitted = (ulong)HeapWithFilters.EnumerateFilteredSegments().Sum(r => (long)r.CommittedMemory.Length);

            Console.WriteLine($"{prefix}GC Allocated Heap Size:    Size: {FormatMemorySize(totalAllocated)} bytes.");
            Console.WriteLine($"{prefix}GC Committed Heap Size:    Size: {FormatMemorySize(totalCommitted)} bytes.");

            return totalCommitted;
        }

        private static void WriteSegmentHeader(TableOutput gcOutput)
        {
            gcOutput.WriteRow("segment", "begin", "allocated", "committed", "allocated size", "committed size");
        }

        private static void WriteSegment(TableOutput gcOutput, ClrSegment segment)
        {
            gcOutput.WriteRow(new DmlDumpHeapSegment(segment),
                segment.ObjectRange.Start, segment.ObjectRange.End, segment.CommittedMemory.End,
                FormatMemorySize(segment.ObjectRange.Length), FormatMemorySize(segment.CommittedMemory.Length));
        }

        private static string FormatMemorySize(ulong length, string zeroValue = "")
        {
            if (length > 0)
            {
                return $"0x{length:x} ({length})";
            }

            return zeroValue;
        }

        private void WriteDivider(char c = '-', int width = 40) => WriteLine(new string(c, width));
    }
}
