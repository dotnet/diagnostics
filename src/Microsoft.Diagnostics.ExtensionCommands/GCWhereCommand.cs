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
    [Command(Name = "gcwhere", Aliases = new[] { "GCWhere" }, Help = "Displays the location in the GC heap of the specified address.")]
    public class GCWhereCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [Argument(Help = "The address on the GC heap to list near objects")]
        public string Address { get; set; }

        public override void Invoke()
        {
            if (!TryParseAddress(Address, out ulong address))
            {
                throw new ArgumentException($"Could not parse address: {Address}");
            }

            // We should only ever find zero or one segments, so the output of the table should only have one entry,
            // but if for some reason the dac reports multiple matching segments (probably due to a bug or inconsistent
            // data), then we do want to print all of those out here.

            ClrSegment[] segments = FindSegments(address).OrderBy(seg => seg.SubHeap.Index).ThenBy(seg => seg.Address).ToArray();
            if (segments.Length == 0)
            {
                Console.WriteLine($"Address {address:x} not found in the managed heap.");
                return;
            }

            Column objectRangeColumn = Range.WithDml(Dml.DumpHeap).GetAppropriateWidth(segments.Select(r => r.ObjectRange));
            Column committedColumn = Range.GetAppropriateWidth(segments.Select(r => r.CommittedMemory));
            Column reservedColumn = Range.GetAppropriateWidth(segments.Select(r => r.ReservedMemory));
            Table output = new(Console, Pointer, IntegerWithoutCommas.WithWidth(6).WithDml(Dml.DumpHeap), DumpHeap, Text.WithWidth(6), objectRangeColumn, committedColumn, reservedColumn);
            output.SetAlignment(Align.Left);
            output.WriteHeader("Address", "Heap", "Segment", "Generation", "Allocated", "Committed", "Reserved");

            foreach (ClrSegment segment in segments)
            {
                string generation;
                if (segment.ReservedMemory.Contains(address))
                {
                    generation = "reserve";
                }
                else
                {
                    generation = segment.GetGeneration(address) switch
                    {
                        Generation.Generation0 => "0",
                        Generation.Generation1 => "1",
                        Generation.Generation2 => "2",
                        Generation.Frozen => "frozen",
                        Generation.Pinned => "pinned",
                        Generation.Large => "large",
                        _ => "???",
                    };
                }

                if (segment.ObjectRange.Contains(address))
                {
                    output.Columns[0] = output.Columns[0].WithDml(Dml.ListNearObj);
                }
                else
                {
                    output.Columns[0] = output.Columns[0].WithDml(null);
                }

                output.WriteRow(address, segment.SubHeap, segment, generation, segment.ObjectRange, segment.CommittedMemory, segment.ReservedMemory);
            }
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"GCWhere displays the location in the GC heap of the argument passed in.

    {prompt}gcwhere 02800038  
    Address  Gen Heap segment  begin    allocated size
    02800038 2    0   02800000 02800038 0282b740  12

When the argument lies in the managed heap, but is not a valid *object* address 
the ""size"" is displayed as 0:

    {prompt}gcwhere 0280003c
    Address  Gen Heap segment  begin    allocated size
    0280003c 2    0   02800000 02800038 0282b740  0

";
        private IEnumerable<ClrSegment> FindSegments(ulong address)
        {
            // ClrHeap.GetSegmentByAddress doesn't search for reserve memory
            return Runtime.Heap.Segments.Where(seg => seg.ObjectRange.Contains(address) || seg.CommittedMemory.Contains(address) || seg.ReservedMemory.Contains(address));
        }
    }
}
