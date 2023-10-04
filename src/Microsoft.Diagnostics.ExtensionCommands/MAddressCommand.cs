// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using static Microsoft.Diagnostics.ExtensionCommands.NativeAddressHelper;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "maddress", Help = "Displays a breakdown of the virtual address space.")]
    public sealed class MAddressCommand : CommandBase
    {
        private const string ImagesFlag = "-images";
        private const string SummaryFlag = "-summary";
        private const string ReserveFlag = "-reserve";
        private const string ReserveHeuristicFlag = "-reserveHeuristic";
        private const string ForceHandleTableFlag = "-forceHandleTable";
        private const string ListFlag = "-list";
        private const string BySizeFlag = "-orderBySize";

        [Option(Name = SummaryFlag, Aliases = new string[] { "-stat", }, Help = "Only print summary table.")]
        public bool Summary { get; set; }

        [Option(Name = ImagesFlag, Help = "Prints a summary table of image memory usage.")]
        public bool ShowImageTable { get; set; }

        [Option(Name = ReserveFlag, Help = "Include MEM_RESERVE regions in the output.")]
        public bool IncludeReserveMemory { get; set; }

        [Option(Name = ReserveHeuristicFlag, Help = "Heuristically tag MEM_RESERVE regions based on adjacent memory regions.")]
        public bool TagReserveMemoryHeuristically { get; set; }

        [Option(Name = ForceHandleTableFlag, Help = "We only tag the HandleTable if we can do so efficiently on newer runtimes.  This option ensures we always tag HandleTable memory, even if it will take a long time.")]
        public bool IncludeHandleTableIfSlow { get; set; }

        [Option(Name = BySizeFlag, Help = "List the raw addresses by size, not by base address.")]
        public bool BySize { get; set; }

        [Option(Name = ListFlag, Help = "A separated list of memory regions to list allocations for.")]
        public string List { get; set; }

        [ServiceImport]
        public NativeAddressHelper AddressHelper { get; set; }

        public override void Invoke()
        {
            if (TagReserveMemoryHeuristically && !IncludeReserveMemory)
            {
                throw new DiagnosticsException($"Cannot use {ReserveHeuristicFlag} without {ReserveFlag}");
            }

            IEnumerable<DescribedRegion> memoryRanges = AddressHelper.EnumerateAddressSpace(tagClrMemoryRanges: true, IncludeReserveMemory, TagReserveMemoryHeuristically, IncludeHandleTableIfSlow);
            if (!IncludeReserveMemory)
            {
                memoryRanges = memoryRanges.Where(m => m.State != MemoryRegionState.MEM_RESERVE);
            }

            DescribedRegion[] ranges = memoryRanges.ToArray();

            // Tag reserved memory based on what's adjacent.
            if (TagReserveMemoryHeuristically)
            {
                CollapseReserveRegions(ranges);
            }

            if (!Summary && List is null)
            {
                Column nameColumn = Text.GetAppropriateWidth(ranges.Select(r => r.Name));
                Column kindColumn = Column.ForEnum<MemoryRegionType>();
                Column stateColumn = Column.ForEnum<MemoryRegionState>();

                // These are flags, so we need a column wide enough for that output instead of ForEnum
                Column protectionColumn = Text.GetAppropriateWidth(ranges.Select(r => r.Protection));
                Column imageColumn = Image.GetAppropriateWidth(ranges.Select(r => r.Image));
                using BorderedTable output = new(Console, nameColumn, Pointer, Pointer, HumanReadableSize, kindColumn, stateColumn, protectionColumn, imageColumn);

                output.WriteHeader("Memory Kind", "StartAddr", "EndAddr-1", "Size", "Type", "State", "Protect", "Image");
                IOrderedEnumerable<DescribedRegion> ordered = BySize ? ranges.OrderByDescending(r => r.Size).ThenBy(r => r.Start) : ranges.OrderBy(r => r.Start);
                foreach (DescribedRegion mem in ordered)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    output.WriteRow(mem.Name, mem.Start, mem.End, mem.Size, mem.Type, mem.State, mem.Protection, mem.Image);
                }
            }

            if (ShowImageTable)
            {
                var imageGroups = from mem in ranges.Where(r => r.State != MemoryRegionState.MEM_RESERVE && r.Image != null)
                                  group mem by mem.Image into g
                                  let Size = g.Sum(k => (long)(k.End - k.Start))
                                  orderby Size descending
                                  select new {
                                      Image = g.Key,
                                      Count = g.Count(),
                                      Size
                                  };

                using (BorderedTable output = new(Console, Image.GetAppropriateWidth(ranges.Select(r => r.Image), max: 80), Integer, HumanReadableSize, ByteCount))
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    output.WriteHeader("Image", "Count", "Size", "Size (bytes)");

                    int count = 0;
                    long size = 0;
                    foreach (var item in imageGroups)
                    {
                        Console.CancellationToken.ThrowIfCancellationRequested();

                        output.WriteRow(item.Image, item.Count, item.Size, item.Size);
                        count += item.Count;
                        size += item.Size;
                    }

                    output.WriteFooter("[TOTAL]", count, size, size);
                }

                Console.WriteLine();
            }


            if (List is not null)
            {
                // Print a list of the specified memory kind, ordered by size descending.

                string[] requested = List.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string kind in requested)
                {
                    if (!ranges.Any(r => r.Name.Equals(kind, StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.WriteLineError($"No memory regions match '{kind}'.");
                    }
                    else
                    {
                        Console.WriteLine($"{kind} Memory Regions:");

                        Table output = new(Console, Pointer, ByteCount, HumanReadableSize, Column.ForEnum<MemoryRegionState>(), Column.ForEnum<MemoryRegionType>(), Column.ForEnum<MemoryRegionProtection>().WithWidth(-1));
                        output.WriteHeader("Base Address", "Size (bytes)", "Size", "Mem State", "Mem Type", "Mem Protect");

                        ulong totalSize = 0;
                        int count = 0;

                        IEnumerable<DescribedRegion> matching = ranges.Where(r => r.Name.Equals(kind, StringComparison.OrdinalIgnoreCase)).OrderByDescending(s => s.Size);
                        foreach (DescribedRegion region in matching)
                        {
                            output.WriteRow(region.Start, region.Size, region.Size, region.State, region.Type, region.Protection);

                            count++;
                            totalSize += region.Size;
                        }

                        Console.WriteLine($"{totalSize:n0} bytes ({totalSize.ConvertToHumanReadable()}) in {count:n0} memory regions");
                        Console.WriteLine();
                    }
                }
            }

            if (List is null || Summary)
            {
                // Show the summary table in almost every case, unless the user specified -list without -summary.

                var grouped = from mem in ranges
                              let name = mem.Name
                              group mem by name into g
                              let Count = g.Count()
                              let Size = g.Sum(f => (long)(f.End - f.Start))
                              orderby Size descending
                              select new {
                                  Name = g.Key,
                                  Count,
                                  Size
                              };

                Column nameColumn = Text.GetAppropriateWidth(ranges.Select(r => r.Name));
                using BorderedTable output = new(Console, nameColumn, Integer, HumanReadableSize, ByteCount);
                output.WriteHeader("Memory Type", "Count", "Size", "Size (bytes)");

                int count = 0;
                long size = 0;
                foreach (var item in grouped)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    output.WriteRow(item.Name, item.Count, item.Size, item.Size);

                    count += item.Count;
                    size += item.Size;
                }

                output.WriteFooter("[TOTAL]", count, size, size);
            }
        }

        [FilterInvoke(Message = "The memory region service does not exists. This command is only supported under windbg/cdb debuggers.")]
        public static bool FilterInvoke([ServiceImport(Optional = true)] NativeAddressHelper helper) => helper != null;

        [HelpInvoke]
        public static string GetDetailedHelp() =>
$@"-------------------------------------------------------------------------------
!maddress is a managed version of !address, which attempts to annotate all memory
with information about CLR's heaps.

usage: !maddress [{SummaryFlag}] [{ImagesFlag}] [{ForceHandleTableFlag}] [{ReserveFlag} [{ReserveHeuristicFlag}]]

Flags:
    {SummaryFlag}
        Show only a summary table of memory regions and not the list of every memory region.

    {ImagesFlag}
        Summarizes the memory ranges consumed by images in the process.

    {ForceHandleTableFlag}
        Ensures that we will always tag HandleTable memory.
        On older versions of CLR, we did not have an efficient way to tag HandleTable
        memory.  As a result, we have to fully enumerate the HandleTable to find
        which regions of memory contain 
        
    {ReserveFlag}
        Include reserved memory (MEM_RESERVE) in the output.  This is usually only
        useful if there is virtual address exhaustion.

    {ReserveHeuristicFlag}
        If this flag is set, then maddress will attempt to ""blame"" reserve segments
        on the region that immediately proceeded it.  For example, if a ""Heap""
        memory segment is immediately followed by a MEM_RESERVE region, we will call
        that reserve region HeapReserve.  Note that this is a heuristic and NOT
        intended to be completely accurate.  This can be useful to try to figure out
        what is creating large amount of MEM_RESERVE regions.

    {ListFlag}
        A separated list of memory region types (as maddress defines them) to print the base
        addresses and sizes of.  This list may be separated by , or ""in quotes"".

    {BySizeFlag}
        Order the list of memory blocks by size (descending) when printing the list
        of all memory blocks instead of by address.
";
    }
}
