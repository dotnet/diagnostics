using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading;
using static Microsoft.Diagnostics.Runtime.DacInterface.SOSDac13;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "eeheap", Aliases = new string[] { "EEHeap" }, Help = "Displays information about native memory that CLR has allocated.")]
    public class EEHeap : CommandBase
    {
        [ServiceImport]
        public IRuntimeService RuntimeService { get; set; }

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }
                
        public override void Invoke()
        {
            IRuntime[] runtimes = RuntimeService.EnumerateRuntimes().ToArray();

            StringBuilder stringBuilder = null;
            foreach (IRuntime iRuntime in RuntimeService.EnumerateRuntimes())
            {
                if (runtimes.Length > 1)
                    WriteDivider($"{iRuntime.RuntimeType} {iRuntime.RuntimeModule?.GetVersionData()}");

                ClrRuntime clrRuntime = iRuntime.Services.GetService<ClrRuntime>();
                PrintOneRuntime(ref stringBuilder, clrRuntime);
            }
        }

        private void PrintOneRuntime(ref StringBuilder stringBuilder, ClrRuntime clrRuntime)
        {
            TableOutput output = new(Console, (21, "x12"), (0, "x12"))
            {
                AlignLeft = true
            };

            PrintAppDomains(output, clrRuntime, out HashSet<ulong> loaderAllocatorsSeen);
            PrintCodeHeaps(output, clrRuntime);
            PrintModuleThunkTable(output, ref stringBuilder, clrRuntime);
            PrintModuleLoaderAllocators(output, ref stringBuilder, clrRuntime, loaderAllocatorsSeen);
            PrintGCHeap(clrRuntime);

            WriteLine();
        }

        private void PrintModuleThunkTable(TableOutput output, ref StringBuilder text, ClrRuntime clrRuntime)
        {
            IEnumerable<ClrModule> modulesWithThunks = clrRuntime.EnumerateModules().Where(r => r.ThunkHeap != 0);
            if (!modulesWithThunks.Any())
                return;

            WriteDivider();
            WriteLine("Module Thunk heaps:");

            PrintModules(output, ref text, modulesWithThunks);
        }

        private void PrintModuleLoaderAllocators(TableOutput output, ref StringBuilder text, ClrRuntime clrRuntime, HashSet<ulong> loaderAllocatorsSeen)
        {
            // On .Net Core, modules share their LoaderAllocator with their AppDomain (and AppDomain shares theirs
            // with SystemDomain).  Only collectable assemblies have unique loader allocators, and that's what we
            // are essentially enumerating here.
            IEnumerable<ClrModule> collectable = from module in clrRuntime.EnumerateModules()
                                                 where module.LoaderAllocator != 0
                                                 where loaderAllocatorsSeen is null || loaderAllocatorsSeen.Contains(module.LoaderAllocator)
                                                 select module;

            if (!collectable.Any())
                return;

            WriteDivider();
            WriteLine("Module LoaderAllocators:");

            PrintModules(output, ref text, collectable);
        }

        private void PrintModules(TableOutput output, ref StringBuilder text, IEnumerable<ClrModule> modules)
        {
            text ??= new(128);
            ulong totalSize = 0, totalWasted = 0;
            foreach (ClrModule module in modules)
            {
                ulong moduleSize = 0, moduleWasted = 0;

                text.Clear();
                foreach (ClrNativeHeapInfo info in module.EnumerateThunkHeap())
                {
                    if (text.Length > 0)
                        text.Append(' ');

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
            output.WriteRow("Total size:", text);
        }

        private void PrintAppDomains(TableOutput output, ClrRuntime clrRuntime, out HashSet<ulong> loaderAllocatorsSeen)
        {
            loaderAllocatorsSeen = null;

            PrintAppDomain(output, clrRuntime.SystemDomain, "System Domain", ref loaderAllocatorsSeen);
            PrintAppDomain(output, clrRuntime.SharedDomain, "Shared Domain", ref loaderAllocatorsSeen);

            for (int i = 0; i < clrRuntime.AppDomains.Count; i++)
            {
                ClrAppDomain appDomain = clrRuntime.AppDomains[i];
                PrintAppDomain(output, appDomain, $"Domain {i + 1}:", ref loaderAllocatorsSeen);
            }
        }

        private void PrintAppDomain(TableOutput output, ClrAppDomain appDomain, string name, ref HashSet<ulong> loaderAllocatorsSeen)
        {
            if (appDomain is null)
                return;

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
                    return;
            }

            var heapsByKind = appDomain.EnumerateLoaderAllocatorHeaps().GroupBy(g => g.Kind).OrderBy(g => GetSortOrder(g.Key));
            PrintAppDomainHeapsByKind(output, heapsByKind);
        }

        private int GetSortOrder(NativeHeapKind key)
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

        private void PrintAppDomainHeapsByKind(TableOutput output, IOrderedEnumerable<IGrouping<NativeHeapKind, ClrNativeHeapInfo>> heapsByKind)
        {
            // Just build and print the table.
            ulong totalSize = 0;
            ulong totalWasted = 0;
            StringBuilder text = new(512);

            foreach (var item in heapsByKind)
            {
                text.Clear();
                NativeHeapKind kind = item.Key;
                ulong heapSize = 0;
                ulong heapWasted = 0;

                foreach (ClrNativeHeapInfo heap in item)
                {
                    if (text.Length > 0)
                        text.Append(" ");

                    (ulong size, ulong wasted) = CalculateSizeAndWasted(text, heap);

                    heapSize += size;
                    heapWasted += wasted;
                }

                text.Append(' ');
                WriteSizeAndWasted(text, heapSize, heapWasted);
                text.Append('.');

                output.WriteRow(kind, text);

                totalSize += heapSize;
                totalWasted += heapWasted;
            }

            text.Clear();
            WriteSizeAndWasted(text, totalSize, totalWasted);

            output.WriteRow("Total size:", text);
            WriteDivider();
        }

        private void PrintCodeHeaps(TableOutput output, ClrRuntime clrRuntime)
        {
            StringBuilder text = new(512);
            foreach (ClrJitManager jitManager in clrRuntime.EnumerateJitManagers())
            {
                output.WriteRow("JIT Manager:", jitManager.Address);

                var heaps = jitManager.EnumerateNativeHeaps().OrderBy(r => r.Kind).ThenBy(r => r.Address);

                ulong totalSize = 0, totalWasted = 0;
                foreach (ClrNativeHeapInfo heap in heaps)
                {
                    text.Clear();

                    (ulong actualSize, ulong wasted) = CalculateSizeAndWasted(text, heap);
                    totalSize += actualSize;
                    totalWasted += wasted;

                    text.Append(' ');
                    WriteSizeAndWasted(text, actualSize, wasted);
                    text.Append('.');

                    output.WriteRow($"{heap.Kind}:", text);
                }

                text.Clear();
                WriteSizeAndWasted(text, totalSize, totalWasted);
                text.Append('.');

                output.WriteRow("Total size:", text);
                WriteDivider();
            }
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
                    wasted = size - actualSize;

                return (actualSize, wasted);
            }

            return (0, 0);
        }

        private static void WriteSizeAndWasted(StringBuilder sb, ulong heapSize, ulong heapWasted)
        {
            sb.Append("Size: ");
            sb.AppendFormat("0x{0:x} ({0:n0}) bytes total", heapSize);

            if (heapWasted > 0)
            {
                sb.AppendFormat(", 0x{0:x} ({0:n0}) bytes wasted", heapWasted);
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

                return actualSize;
            }

            return 0;
        }



        private void PrintGCHeap(ClrRuntime clrRuntime)
        {
            TableOutput segmentTable = new(Console, (8, ""), (9, ""), (14, "x12"), (14, "x12"), (14, "x12"), (14, "x12"), (24, ""), (24, ""), (24, ""));
            TableOutput ephemeralSegmentTable = new(Console, (8, ""), (-9, ""), (14, "x12"), (14, "x12"), (-24, "x12"));

            var segmentByHeap = from seg in clrRuntime.Heap.Segments
                                group seg by seg.LogicalHeap into g
                                let Segments = g.OrderBy(GetSegmentOrder).ThenBy(seg => seg.CommittedMemory.Start)
                                let TotalCommitted = (ulong)g.Sum(seg => (long)seg.CommittedMemory.Length)
                                let TotalAllocated = (ulong)g.Sum(seg => (long)seg.ObjectRange.Length)
                                let TotalReserved = (ulong)g.Sum(seg => (long)seg.ReservedMemory.Length)
                                select new
                                {
                                    Heap = g.Key,
                                    Segments,
                                    TotalCommitted,
                                    TotalAllocated,
                                    TotalReserved
                                };

            int heapTotal = 0;
            ulong totalAllocated = 0;
            ulong totalCommitted = 0;
            ulong totalReserved = 0;
            foreach (var heapSegments in segmentByHeap)
            {
                segmentTable.WriteRowWithSpacing('-', "[ Heap ]", "[ Kind ]", "[ Begin ]", "[ Allocated ]", "[ Committed ]", "[ Reserved ]", "[ Allocated Size ]", "[ Committed Size ]", "[ Reserved Size ]");

                foreach (ClrSegment segment in heapSegments.Segments)
                {
                    segmentTable.WriteRow(
                        segment.LogicalHeap,
                        GetSegmentKind(segment),
                        segment.ObjectRange.Start,
                        segment.ObjectRange.End,
                        segment.CommittedMemory.End,
                        segment.ReservedMemory.End,
                        FormatSegmentSize(segment.ObjectRange.Length),
                        FormatSegmentSize(segment.CommittedMemory.Length),
                        FormatSegmentSize(segment.Length)
                        );

                    if (segment.IsEphemeralSegment)
                    {
                        ephemeralSegmentTable.WriteRow("", "-> gen0", segment.Generation0.Start, segment.Generation0.End, "  " + FormatSegmentSize(segment.Generation0.Length));
                        ephemeralSegmentTable.WriteRow("", "-> gen1", segment.Generation1.Start, segment.Generation1.End, "  " + FormatSegmentSize(segment.Generation1.Length));
                        ephemeralSegmentTable.WriteRow("", "-> gen2", segment.Generation2.Start, segment.Generation2.End, "  " + FormatSegmentSize(segment.Generation2.Length));
                    }
                }

                string footer = $" [ HEAP {heapSegments.Heap} ] ---- [ ALLOCATED: {FormatSegmentSize(heapSegments.TotalAllocated)} ] ---- [ COMMITTED: {FormatSegmentSize(heapSegments.TotalCommitted)} ] ---- [ RESERVED: {FormatSegmentSize(heapSegments.TotalReserved)} ] ";
                Console.WriteLine(footer.PadLeft(segmentTable.TotalWidth / 2 + footer.Length / 2, '-').PadRight(segmentTable.TotalWidth, '-'));
                Console.WriteLine();

                heapTotal++;
                totalAllocated += heapSegments.TotalAllocated;
                totalCommitted += heapSegments.TotalCommitted;
                totalReserved += heapSegments.TotalReserved;
            }

            int hexWidth = Math.Max(totalCommitted.ToString("x").Length, totalReserved.ToString("x").Length) + 2;

            TableOutput totalTable = new(Console, (16, ""), (hexWidth, ""), (32, "")) { AlignLeft = true };

            totalTable.WriteRow("Total GC Heaps:", heapTotal);
            totalTable.WriteRow("Total Allocated:", $"0x{totalAllocated:x}", $"({totalAllocated.ConvertToHumanReadable()})");
            totalTable.WriteRow("Total Committed:", $"0x{totalCommitted:x}", $"({totalCommitted.ConvertToHumanReadable()})");
            totalTable.WriteRow("Total Reserved: ", $"0x{totalReserved:x}", $"({totalReserved.ConvertToHumanReadable()})");
        }

        static string FormatSegmentSize(ulong length)
        {
            if (length > 0)
                return $"0x{length:x} ({length.ConvertToHumanReadable()})";
            return "";
        }

        string GetSegmentKind(ClrSegment segment)
        {
            if (segment.IsEphemeralSegment)
                return "ephemeral";
            if (segment.IsLargeObjectSegment)
                return "large";
            if (segment.IsPinnedObjectSegment)
                return "pinned";
            return "gen2";
        }

        int GetSegmentOrder(ClrSegment seg)
        {
            if (seg.IsEphemeralSegment)
                return 3;

            if (seg.IsLargeObjectSegment)
                return 0;
            if (seg.IsPinnedObjectSegment)
                return 1;

            return 2;
        }

        private void WriteDivider(int width = 120) => WriteLine(new string('-', width));

        private void WriteDivider(string header, int width = 120)
        {
            int lhs = (width - header.Length - 2) / 2;
            if (lhs < 0)
            {
                WriteLine(header);
                return;
            }

            int rhs = lhs;
            if ((header.Length % 2) == 1)
                rhs++;

            StringBuilder sb = new(width + 1);
            sb.Append('-', lhs);
            sb.Append(' ');
            sb.Append(header);
            sb.Append(' ');
            sb.Append('-', rhs);

            WriteLine(sb.ToString());
        }
    }
}
