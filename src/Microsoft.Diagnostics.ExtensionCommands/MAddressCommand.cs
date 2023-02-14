using Microsoft.Diagnostics.DebugServices;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.Diagnostics.ExtensionCommands.NativeAddressHelper;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "maddress", Help = "Displays a breakdown of the virtual address space.")]
    public sealed class MAddressCommand : CommandBase
    {
        [Option(Name = "--list", Aliases = new string[] { "-l", "--all", }, Help = "Prints the full list of annotated memory regions.")]
        public bool ListAll { get; set; }

        [Option(Name = "--images", Aliases = new string[] { "-i", "-image", "-img", "--img" }, Help = "Prints the table of image memory usage.")]
        public bool ShowImageTable { get; set; }

        [Option(Name = "--includeReserve", Help = "Include MEM_RESERVE regions in the output.")]
        public bool IncludeReserveMemory { get; set; }

        [Option(Name = "--tagReserve", Help = "Heuristically tag MEM_RESERVE regions based on adjacent memory regions.")]
        public bool TagReserveMemoryHeuristically { get; set; }

        [ServiceImport]
        public NativeAddressHelper AddressHelper { get; set; }

        public override void Invoke()
        {
            if (TagReserveMemoryHeuristically && !IncludeReserveMemory)
                throw new DiagnosticsException("Cannot use --tagReserve without --includeReserve");

            PrintMemorySummary(ListAll, ShowImageTable, IncludeReserveMemory, TagReserveMemoryHeuristically);
        }

        public void PrintMemorySummary(bool printAllMemory, bool showImageTable, bool includeReserveMemory, bool tagReserveMemoryHeuristically)
        {
            IEnumerable<DescribedRegion> memoryRanges = AddressHelper.EnumerateAddressSpace(tagClrMemoryRanges: true, includeReserveMemory, tagReserveMemoryHeuristically);
            if (!includeReserveMemory)
                memoryRanges = memoryRanges.Where(m => m.State != MemoryRegionState.MEM_RESERVE);

            DescribedRegion[] ranges = memoryRanges.ToArray();

            int nameSizeMax = ranges.Max(r => r.Name.Length);

            // Tag reserved memory based on what's adjacent.
            if (tagReserveMemoryHeuristically)
                CollapseReserveRegions(ranges);

            if (printAllMemory)
            {
                int kindSize = ranges.Max(r => r.Type.ToString().Length);
                int stateSize = ranges.Max(r => r.State.ToString().Length);
                int protectSize = ranges.Max(r => r.Protection.ToString().Length);

                TableOutput output = new(Console, (nameSizeMax, ""), (12, "x"), (12, "x"), (12, ""), (kindSize, ""), (stateSize, ""), (protectSize, ""))
                {
                    AlignLeft = true,
                    Divider = " | "
                };

                output.WriteRowWithSpacing('-', "Memory Kind", "StartAddr", "EndAddr-1", "Size", "Type", "State", "Protect", "Image");
                foreach (DescribedRegion mem in ranges)
                    output.WriteRow(mem.Name, mem.Start, mem.End, mem.Length.ConvertToHumanReadable(), mem.Type, mem.State, mem.Protection, mem.Image);

                output.WriteSpacer('-');
            }

            if (showImageTable)
            {
                var imageGroups = from mem in ranges.Where(r => r.State != MemoryRegionState.MEM_RESERVE && r.Image != null)
                                  group mem by mem.Image into g
                                  let Size = g.Sum(k => (long)(k.End - k.Start))
                                  orderby Size descending
                                  select new
                                  {
                                      Image = g.Key,
                                      Count = g.Count(),
                                      Size
                                  };

                int moduleLen = Math.Max(80, ranges.Max(r => r.Image?.Length ?? 0));

                TableOutput output = new(Console, (moduleLen, ""), (8, "n0"), (12, ""), (24, "n0"))
                {
                    Divider = " | "
                };

                output.WriteRowWithSpacing('-', "Image", "Regions", "Size", "Size (bytes)");

                int count = 0;
                long size = 0;
                foreach (var item in imageGroups)
                {
                    output.WriteRow(item.Image, item.Count, item.Size.ConvertToHumanReadable(), item.Size);
                    count += item.Count;
                    size += item.Size;
                }

                output.WriteSpacer('-');
                output.WriteRow("[TOTAL]", count, size.ConvertToHumanReadable(), size);
                WriteLine("");
            }


            // Print summary table unconditionally
            {
                var grouped = from mem in ranges
                              let name = mem.Name
                              group mem by name into g
                              let Count = g.Count()
                              let Size = g.Sum(f => (long)(f.End - f.Start))
                              orderby Size descending
                              select new
                              {
                                  Name = g.Key,
                                  Count,
                                  Size
                              };

                TableOutput output = new(Console, (-nameSizeMax, ""), (8, "n0"), (12, ""), (24, "n0"))
                {
                    Divider = " | "
                };

                output.WriteRowWithSpacing('-', "Region Type", "Count", "Size", "Size (bytes)");

                int count = 0;
                long size = 0;
                foreach (var item in grouped)
                {
                    output.WriteRow(item.Name, item.Count, item.Size.ConvertToHumanReadable(), item.Size);
                    count += item.Count;
                    size += item.Size;
                }

                output.WriteSpacer('-');
                output.WriteRow("[TOTAL]", count, size.ConvertToHumanReadable(), size);
            }
        }


        [HelpInvoke]
        public void HelpInvoke()
        {
            WriteLine(
@"-------------------------------------------------------------------------------
!maddress is a managed version of !address, which attempts to annotate all memory
with information about CLR's heaps.

usage: !maddress [--list] [--images] [--includeReserve [--tagReserve]]

Flags:
    --list
        Shows the full list of annotated memory regions and not just the statistics
        table.

    --images
        Summarizes the memory ranges consumed by images in the process.
        
    --includeReserve
        Include reserved memory (MEM_RESERVE) in the output.  This is usually only
        useful if there is virtual address exhaustion.

    --tagReserve
        If this flag is set, then !maddress will attempt to ""blame"" reserve segments
        on the region that immediately proceeded it.  For example, if a ""Heap""
        memory segment is immediately followed by a MEM_RESERVE region, we will call
        that reserve region HeapReserve.  Note that this is a heuristic and NOT
        intended to be completely accurate.  This can be useful to try to figure out
        what is creating large amount of MEM_RESERVE regions.
");
        }
    }
}
